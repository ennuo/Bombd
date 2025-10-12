using Bombd.Extensions;
using Bombd.Globals;
using Bombd.Helpers;
using Bombd.Logging;
using Bombd.Protocols;
using Bombd.Serialization;
using Bombd.Types.Gateway;
using Bombd.Types.Network;
using Bombd.Types.Network.Arbitration;
using Bombd.Types.Network.Messages;
using Bombd.Types.Network.Objects;
using Bombd.Types.Network.Races;
using Bombd.Types.Network.Room;
using Bombd.Types.Network.Simulation;

namespace Bombd.Core;

public class SimServer
{
    public int Owner { get; private set; }
    
    public readonly GameRoom Room;
    public readonly Platform Platform;
    public readonly ServerType Type;
    public readonly bool IsRanked;
    
    // Needs to be able to be set by the subclass because top track races in ModNation
    // don't get created with series race attributes set.
    public bool IsSeries { get; private set; }
    
    public readonly bool IsModNation;
    public readonly bool IsKarting;
    
    public RaceState RaceState { get; private set; } = RaceState.Invalid;
    public bool HasRaceSettings => _raceSettings != null;
    public bool HasSeriesSetting => _seriesInfo != null;

    private readonly List<GamePlayer> _players = [];
    private readonly List<PlayerInfo> _playerInfos = [];
    private readonly List<PlayerState> _playerStates = [];
    private readonly Dictionary<int, GamePlayer> _playerLookup = new();
    private readonly NetArbitrationServer _arbitrationServer;
    
    private readonly Dictionary<int, SyncObject> _syncObjects = new();
    private int _seed = CryptoHelper.GetRandomSecret();
    
    private readonly RaceConstants _raceConstants;
    private readonly List<EventResult> _eventResults = [];
    private int _raceStateStartTime;
    private int _raceStateEndTime;
    private float _pausedTimeRemaining;
    private string _destination = Destination.GameRoom;
    
    private GenericSyncObject<CoiInfo>? _coiInfo;
    private GenericSyncObject<VotePackage> _votePackage;
    private GenericSyncObject<GameroomState> _gameroomState;
    private GenericSyncObject<AiInfo> _aiInfo;
    private GenericSyncObject<SpectatorInfo> _spectatorInfo;
    private GenericSyncObject<StartingGrid> _startingGrid;
    private GenericSyncObject<EventSettings>? _raceSettings;
    private GenericSyncObject<SeriesInfo>? _seriesInfo;
    
    public SimServer(ServerType type, GameRoom room, int owner, bool isRanked, bool isSeries)
    {
        Platform = room.Platform;
        Type = type;
        Room = room;
        Owner = owner;
        IsRanked = isRanked;
        IsSeries = isSeries;

        IsModNation = Platform == Platform.ModNation;
        IsKarting = Platform == Platform.Karting;
        
        if (IsModNation)
            _raceConstants = isRanked ? RaceConstants.Ranked : RaceConstants.ModNation;
        else _raceConstants = RaceConstants.Karting;

        _arbitrationServer = new NetArbitrationServer(OnReleaseArbitratedItem);
        
        Logger.LogInfo<SimServer>($"Starting SimServer ({Type}:{Platform}, IsRanked = {isRanked}, IsSeries = {isSeries})");
        
        if (Type == ServerType.KartPark)
        {
            _coiInfo = CreateSystemSyncObject(WebApiManager.GetCircleOfInfluence(), NetObjectType.NetCoiInfoPackage);
        }

        if (Type == ServerType.Competitive)
        {
            if (IsKarting)
                _votePackage = CreateSystemSyncObject(new VotePackage(), NetObjectType.VotePackage);
            _gameroomState = CreateSystemSyncObject(new GameroomState(Platform), NetObjectType.GameroomState);
            _spectatorInfo = CreateSystemSyncObject(new SpectatorInfo(Platform), NetObjectType.SpectatorInfo);
            _aiInfo = CreateSystemSyncObject(new AiInfo(Platform), NetObjectType.AiInfo);
            _startingGrid = CreateSystemSyncObject(new StartingGrid(Platform), NetObjectType.StartingGrid);
            
            if (!isRanked) return;

            EventSettings raceSettings;
            if (isSeries)
            {
                var series = Career.ModNation.GetRankedSeries(5, Owner);
                _seriesInfo = CreateSystemSyncObject(series, NetObjectType.SeriesInfo);
                raceSettings = series.Events.First();
            }
            else
            {
                raceSettings = Career.ModNation.GetRankedEvent(Owner);
            }
            
            _raceSettings = CreateSystemSyncObject(raceSettings, NetObjectType.RaceSettings);
            Room.UpdateAttributes(raceSettings);
        }
    }

    public bool IsRoomReady()
    {
        if (Owner == -1) return true;
        if (!_playerLookup.TryGetValue(Owner, out GamePlayer? player)) return false;
        
        var state = player.State;
        if (Type != ServerType.Competitive) return !state.IsConnecting;
        return !state.IsConnecting && (state.Flags & PlayerStateFlags.GameRoomReady) != 0;
    }

    private void SwitchAllToRacers()
    {
        foreach (GamePlayer player in _players)
            player.IsSpectator = false;
        foreach (PlayerInfo info in _playerInfos)
            info.Operation = PlayerJoinStatus.RacerPending;
        
        if (IsModNation)
            Broadcast(new NetMessageSessionInfo(PlayerSessionOperation.SwitchAllToRacer), NetMessageType.PlayerSessionInfo);
        else
            BroadcastSessionInfo();
    }

    private void SwitchToSpectator(GamePlayer player)
    {
        if (player.IsSpectator) return;
        
        player.IsSpectator = true;
        if (IsKarting) BroadcastSessionInfo();
        else
        {
            Broadcast(new NetMessageSessionInfo(PlayerSessionOperation.SwitchRacerToSpectator, player.UserId), 
                NetMessageType.PlayerSessionInfo);   
        }
        
        if (RaceState == RaceState.GameroomCountdown)
            UpdateRaceSetup();
    }

    private void SwitchToRacer(GamePlayer player)
    {
        if (!player.IsSpectator) return;

        player.IsSpectator = false;
        if (IsKarting) BroadcastSessionInfo();
        else
        {
            Broadcast(new NetMessageSessionInfo(PlayerSessionOperation.SwitchSpectatorToRacer, player.UserId),
                NetMessageType.PlayerSessionInfo);
        }

        if (RaceState == RaceState.GameroomCountdown)
            UpdateRaceSetup();
    }

    private void BroadcastSessionInfo()
    {
        // In ModNation, the session messages seem to only be switches and joins rather than their actual states,
        // so re-sending them might cause issues?
        if (IsModNation) return;
        
        Broadcast(new NetMessagePlayerInfo { Data = _playerInfos }, NetMessageType.PlayerSessionInfo);
    }
    
    public void OnPlayerJoin(GamePlayer player)
    {
        // Don't actually know if this is random per room or random per player,
        // I assume it's used for determinism, but I don't know?
        player.SendMessage(_seed, NetMessageType.RandomSeed);
        
        // Add player to lookup cache
        _playerLookup[player.UserId] = player;
        _playerStates.Add(player.State);
        _players.Add(player);
        
        if (IsModNation)
        {
            player.IsSpectator = !CanJoinAsRacer();
            PlayerSessionOperation operation = 
                player.IsSpectator ? PlayerSessionOperation.JoinAsSpectator : PlayerSessionOperation.JoinAsRacer;
            
            // Tell everybody in the game room about our session state
            Broadcast(new NetMessageSessionInfo(operation, player.UserId), NetMessageType.PlayerSessionInfo);
            // Tell the joining player about everybody else who is in the game room
            foreach (GamePlayer peer in _players)
            {
                if (player == peer) continue;
                operation = peer.IsSpectator ? PlayerSessionOperation.JoinAsSpectator : PlayerSessionOperation.JoinAsRacer;
                player.SendMessage(new NetMessageSessionInfo(operation, peer.UserId), NetMessageType.PlayerSessionInfo);
            }
        }
        else
        {
            // In Karting, we can just send the current player infos, our state will get sent when the client sends
            // an initial PlayerCreateInfo message
            player.SendMessage(new NetMessagePlayerInfo { Data = _playerInfos }, NetMessageType.PlayerCreateInfo);
        }

        // Initialize any sync objects that exist
        foreach (SyncObject syncObject in _syncObjects.Values)
        {
            // Needs to be sent twice with a create and update message,
            // they use the same net message type in Modnation despite having
            // separate network event types, but whatever.
            SendSyncObjectMessage(syncObject, NetObjectMessageType.Create, player);

            // If the segment is empty, it probably hasn't been initialized
            // by the owner yet, don't send it.
            if (syncObject.Data.Count != 0)
                SendSyncObjectMessage(syncObject, NetObjectMessageType.Update, player);
        }
    }
    
    public void OnPlayerLeft(GamePlayer player, bool disconnected)
    {
        // Destroy cached states
        _playerStates.Remove(player.State);
        _playerLookup.Remove(player.UserId);
        _playerInfos.RemoveAll(info => info.NetcodeUserId == player.UserId);
        _players.Remove(player);
        
        // Destroy all sync objects owned by the player
        IEnumerable<KeyValuePair<int, SyncObject>>
            owned = _syncObjects.Where(x => x.Value.OwnerUserId == player.UserId);
        foreach (KeyValuePair<int, SyncObject> pair in owned)
        {
            SendSyncObjectMessage(pair.Value, NetObjectMessageType.Remove);
            _syncObjects.Remove(pair.Key);
        }
        
        // Send a leave reason to everybody else in the session
        Broadcast(new NetMessagePlayerLeave(Platform) { PlayerName = player.Username, Reason = player.LeaveReason }, NetMessageType.PlayerLeave);

        if (Type == ServerType.Competitive && !player.IsSpectator)
        {
            // Tell the PlayerConnect server if the player left in the middle of a race
            if (IsModNation && RaceState is >= RaceState.LoadingIntoRace and < RaceState.PostRace)
                BombdServer.Comms.NotifyPlayerQuit(player.State.PlayerConnectId, disconnected);
            // Remove the player from the starting grid if we're still in the pre-game phase
            if (RaceState == RaceState.GameroomCountdown)
                UpdateRaceSetup();
        }
        
        if (_players.Count == 0) return;
        
        // Make sure to re-order the pod if necessary
        if (Type == ServerType.Pod) RecalculatePodPositions();
        
        // If we're in Karting and in the voting stage, make sure to remove our vote
        if (IsKarting && Type == ServerType.Competitive && _votePackage.Value.IsVoting)
        {
            _votePackage.Value.RemoveVote(player.UserId);
        }
        
        // If we're the owner, change the host to someone random
        if (player.UserId == Owner)
        {
            var random = new Random();
            int index = random.Next(0, _players.Count);
            var randomPlayer = _players[index];

            Owner = randomPlayer.UserId;
            if (_raceSettings != null)
            {
                _raceSettings.Value.OwnerNetcodeUserId = Owner;
                TriggerRaceEventSync(EventUpdateReason.HostChanged);
                if (_seriesInfo != null)
                {
                    foreach (var evt in _seriesInfo.Value.Events)
                        evt.OwnerNetcodeUserId = Owner;
                }
            }
        }
    }

    public void OnHotSeatPlaylistRefresh(EventSettings playlist)
    {
        // Don't care about the hot seat refresh if we're not in a modspot.
        if (_coiInfo == null) return;
        
        _coiInfo.Value.Hotseat.Event = playlist;
        _coiInfo.Sync();
    }

    private void OnReleaseArbitratedItem(ItemNode item, ItemAcquirerNode acquirer)
    {
        var message = new NetMessageArbitratedItem(Platform)
        {
            ItemType = item.TypeId,
            ItemId = item.Uid,
            PlayerNameUid = acquirer.Uid
        };

        Broadcast(message, NetMessageType.ArbitratedItemRelease);
    }

    private void RecalculatePodPositions()
    {
        if (!IsKarting) return;
        
        int position = 1;
        foreach (PlayerInfo info in _playerInfos)
           info.PodLocation = $"POD_Player0{position++}_Placer";
        
        // Send the new session info to everybody in the game
        BroadcastSessionInfo();
    }

    private void BroadcastPlayerState()
    {
        Broadcast(new NetMessagePlayerUpdate(Platform) { Data = _playerStates }, NetMessageType.BulkPlayerStateUpdate);
    }
    
    private void BroadcastVoipData(ArraySegment<byte> data, GamePlayer sender)
    {
        foreach (GamePlayer player in _players)
        {
            if (player == sender) continue;
            player.Send(data, PacketType.VoipData);
        }
    }

    private void BroadcastBlock(ArraySegment<byte> data, bool reliable, GamePlayer? sender = null)
    {
        using NetworkWriterPooled writer = NetworkWriterPool.Get();
        ArraySegment<byte> message = NetworkMessages.PackData(writer, data,
            reliable ? NetMessageType.MessageReliableBlock : NetMessageType.MessageUnreliableBlock);
        PacketType type = reliable ? PacketType.ReliableGameData : PacketType.UnreliableGameData;
        foreach (GamePlayer player in _players)
        {
            if (player == sender) continue;
            player.Send(message, type);
        }
    }

    private void Broadcast(int data, NetMessageType type, GamePlayer? sender = null)
    {
        using NetworkWriterPooled writer = NetworkWriterPool.Get();
        ArraySegment<byte> message = NetworkMessages.PackInt(writer, data, type);
        foreach (GamePlayer player in _players)
        {
            if (player == sender) continue;
            player.Send(message, PacketType.ReliableGameData);
        }
    }
    
    private void Broadcast(ArraySegment<byte> data, NetMessageType type, GamePlayer? sender = null)
    {
        using NetworkWriterPooled writer = NetworkWriterPool.Get();
        ArraySegment<byte> message = NetworkMessages.PackData(writer, data, type);
        foreach (GamePlayer player in _players)
        {
            if (player == sender) continue;
            player.Send(message, PacketType.ReliableGameData);   
        }
    }
    
    private void Broadcast(INetworkWritable data, NetMessageType type, GamePlayer? sender = null)
    {
        using NetworkWriterPooled writer = NetworkWriterPool.Get();
        ArraySegment<byte> message = NetworkMessages.Pack(writer, data, type);
        foreach (GamePlayer player in _players)
        {
            if (player == sender) continue;
            player.Send(message, PacketType.ReliableGameData);
        }
    }

    private void UpdateVotePackage()
    {
        if (_raceSettings == null) return;
        
        _votePackage.Value.StartVote(_raceSettings.Value.CreationId);
        _votePackage.Sync();
    }

    private void StartEvent()
    {
        if (Type != ServerType.Competitive || _raceSettings == null) return;
        RaceState = RaceState.LoadingIntoRace;
        
        if (IsModNation)
        {
            int trackId = _raceSettings.Value.CreationId;
            List<int> playerIds = _players.Where(player => !player.IsSpectator)
                .Select(player => player.State.PlayerConnectId).ToList();
            BombdServer.Comms.NotifyEventStarted(trackId, playerIds);   
        }
    }
    
    private void SetCurrentGameroomState(RoomState state)
    {
        var room = _gameroomState.Value;
        var oldState = room.State;
        if (state == oldState) return;
        
        Logger.LogDebug<SimServer>($"Setting GameRoomState to {state}");

        // Make sure we cache how much time is remaining when we paused the countdown
        if (state == RoomState.CountingDownPaused)
        {
            _pausedTimeRemaining = room.LoadEventTime - TimeHelper.LocalTime;
        }
        
        room.State = state;
        room.LoadEventTime = 0;
        room.LockedTimerValue = 0.0f;
        room.LockedForRacerJoinsValue = 0.0f;
        
        // Reset the race loading flags for each player
        if (state <= RoomState.Ready)
        {
            foreach (var player in _players)
                player.State.Flags &= ~PlayerStateFlags.RaceLoadFlags;
        }
        
        RaceState = RaceState.Invalid;
        switch (state)
        {
            case RoomState.None:
            { 
                BroadcastPlayerState();
                SwitchAllToRacers();
                if (_raceSettings != null)
                    Room.UpdateAttributes(_raceSettings.Value);
                break;
            }
            case RoomState.RaceInProgress:
            {
                StartEvent();
                BroadcastSessionInfo();
                BroadcastPlayerState();
                if (_raceSettings != null)
                    Room.UpdateAttributes(_raceSettings.Value);
                break;
            }
            case RoomState.Ready:
            {
                // If the gameroom isn't loaded before it receives the player create info,
                // it won't ever update the racer state
                SwitchAllToRacers();
                break;
            }
            case RoomState.CountingDownPaused:
            {
                RaceState = RaceState.GameroomCountdown;
                break;
            }
            case RoomState.CountingDown:
            {
                UpdateRaceSetup();
                
                room.LoadEventTime = TimeHelper.LocalTime + _raceConstants.GameRoomCountdownTime;
                room.LockedForRacerJoinsValue = _raceConstants.GameRoomTimerRacerLock;
                room.LockedTimerValue = _raceConstants.GameRoomTimerLock;   

                // Restore the old timer state pre-pause
                if (oldState == RoomState.CountingDownPaused)
                {
                    room.LoadEventTime = TimeHelper.LocalTime + (int)_pausedTimeRemaining;
                }
                
                RaceState = RaceState.GameroomCountdown;
                _raceStateEndTime = room.LoadEventTime;
                
                break;
            }
        }
        
        _gameroomState.Sync();
    }
    
    private GenericSyncObject<T> CreateSystemSyncObject<T>(T instance, NetObjectTypeInfo typeInfo) where T : INetworkWritable
    {
        return CreateGenericSyncObject(instance, typeInfo, NetworkMessages.SimServerName, -1);
    }
    
    private GenericSyncObject<T> CreateGenericSyncObject<T>(T instance, NetObjectTypeInfo typeInfo, string owner, int userId) where T : INetworkWritable
    {
        int type = typeInfo.ModnationTypeId;
        if (IsKarting)
            type = typeInfo.KartingTypeId;
        
        var syncObject = new GenericSyncObject<T>(instance, type, owner, userId);
        _syncObjects[syncObject.Guid] = syncObject;
        syncObject.OnUpdate = () =>
        {
            SendSyncObjectMessage(syncObject, NetObjectMessageType.Update);
        };
        
        // If there are players in the game room, make sure to send the newly created sync object to them
        if (_players.Count != 0)
        {
            SendSyncObjectMessage(syncObject, NetObjectMessageType.Create);
            SendSyncObjectMessage(syncObject, NetObjectMessageType.Update);
        }
        
        Logger.LogInfo<SimServer>($"{syncObject} has been created by the system.");
        
        return syncObject;
    }

    private void UpdateRaceSetup()
    {
        if (_raceSettings == null || Type != ServerType.Competitive || _players.Count == 0) return;

        _startingGrid.Value.Clear();
        foreach (GamePlayer player in _players)
        {
            if (!player.State.HasNameUid) continue;
            _startingGrid.Value.Add(new GridPositionData(player.State.NameUid, false));
            if (player.Guest != null)
                _startingGrid.Value.Add(new GridPositionData(player.Guest.NameUid, true));
        }

        int maxAi = _aiInfo.Value.DataSet.Length;
        int maxHumans = _raceSettings.Value.MaxHumans;
        int numAi = Math.Min(maxAi, maxHumans - _startingGrid.Value.Count);
        if (numAi <= 0) numAi = 0;
        
        // AI are owned by players, if one player disconnects, their AI is destroyed,
        // so distribute them between all players
        List<string> playerNames = _players.Select(player => player.Username).ToList();
        _aiInfo.Value = _raceSettings.Value.AiEnabled ? 
            new AiInfo(Platform, playerNames, numAi) : new AiInfo(Platform);

        for (int i = 0; i < _aiInfo.Value.Count; ++i)
        {
            _startingGrid.Value.Add(new GridPositionData(
                _aiInfo.Value.DataSet[i].NameUid,
                false
            ));
        }
        
        _startingGrid.Sync();
    }
    
    public bool CanJoinAsRacer()
    {
        if (Type != ServerType.Competitive) return true;
        var room = _gameroomState.Value;
        
        if (room.State == RoomState.CountingDown)
        {
            int pointOfNoReturn = room.LoadEventTime - (int)room.LockedForRacerJoinsValue;
            if (TimeHelper.LocalTime >= pointOfNoReturn)
                return false;
        }
        
        return room.State != RoomState.RaceInProgress;
    }

    private SyncObject? GetOwnedSyncObject(int guid, GamePlayer player)
    {
        if (!_syncObjects.TryGetValue(guid, out SyncObject? syncObject))
        {
            Logger.LogWarning<SimServer>($"{player.Username} tried to perform an operation on a SyncObject that doesn't exist! ({guid})");
            return null;
        }
        
        if (syncObject.OwnerUserId != player.UserId)
        {
            Logger.LogWarning<SimServer>($"{player.Username} tried to perform an operation on a SyncObject that they don't own!");
            return null;
        }

        return syncObject;
    }
    
    private void SendSyncObjectMessage(SyncObject syncObject, NetObjectMessageType messageType,
        GamePlayer? recipient = null)
    {
        INetworkWritable message;
        var type = NetMessageType.SyncObjectCreate;
        if (IsKarting)
        {
            switch (messageType)
            {
                case NetObjectMessageType.Create:
                {
                    message = new NetMessageSyncObjectCreate(syncObject);
                    break;
                }
                case NetObjectMessageType.Update:
                {
                    message = new NetMessageSyncObjectUpdate(syncObject);
                    type = NetMessageType.SyncObjectUpdate;
                    break;
                }
                case NetObjectMessageType.Remove:
                {
                    message = new NetMessageSyncObjectRemove(syncObject);
                    type = NetMessageType.SyncObjectRemove;
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(messageType), messageType, null);
            }
        }
        else message = new NetMessageSyncObject(syncObject, messageType);
        
        if (recipient != null) recipient.SendMessage(message, type);
        else Broadcast(message, type);
    }
    
    private bool PersistUserSyncedObject(SyncObject syncObject, GamePlayer player)
    {
        if (_syncObjects.ContainsKey(syncObject.Guid))
        {
            Logger.LogWarning<SimServer>($"{player.Username} tried to create a SyncObject with a GUID that already exists!");
            return false;
        }
        
        Logger.LogDebug<SimServer>(
            $"Creating SyncObject({syncObject}) with type {syncObject.Type} and owner {syncObject.OwnerName}");
        _syncObjects[syncObject.Guid] = syncObject;
        return true;
    }

    private void ParsePlayerConfig(ArraySegment<byte> data, GamePlayer player)
    {
        try
        {
            var config = PlayerConfig.ReadVersioned(data, Platform);

            // Type 1 is guest, we need to extract the name uid on ModNation
            if (config.Type == 1)
            {
                GameGuest? guest = player.GetGuestByName(config.Username);
                if (guest == null || (IsKarting && config.NetcodeUserId != -1) ||
                    (IsModNation && config.NetcodeUserId != player.UserId))
                {
                    Logger.LogWarning<SimServer>(
                        $"Guest doesn't belong to {player.Username}, this shouldn't happen, disconnecting!");
                    player.Disconnect();
                    return;
                }

                // On Karting, we already got the NameUid from the PlayerInfoCreate message
                if (IsModNation)
                    guest.NameUid = CryptoHelper.StringHashU32(config.UidName);
            }
            else if (config.Type == 0)
            {
                uint nameUid = CryptoHelper.StringHashU32(config.UidName);
                if (config.NetcodeUserId != player.UserId || nameUid != player.State.NameUid)
                {
                    Logger.LogWarning<SimServer>(
                        $"PlayerConfig doesn't belong to {player.Username}, this shouldn't happen, disconnecting!");
                    player.Disconnect();
                    return;
                }
                
                if (IsModNation)
                    BombdServer.Comms.UpdatePlayerData(player.State.PlayerConnectId, config.CharCreationId, config.KartCreationId);
                player.State.WaitingForPlayerConfig = false;
            }
            else
            {
                Logger.LogWarning<SimServer>(
                    $"PlayerConfig for {player.Username} has invalid type! Disconnecting!");
                player.Disconnect();
            }
        }
        catch (Exception)
        {
            Logger.LogError<SimServer>(
                $"Failed to parse PlayerConfig for {player.Username}'s guest, disconnecting them from the game session.");
            player.Disconnect();
        }
    }
    
    private bool UpdateUserSyncObject(int guid, ArraySegment<byte> data, GamePlayer player)
    {
        SyncObject? syncObject = GetOwnedSyncObject(guid, player);
        if (syncObject == null) return false;
        
        Logger.LogDebug<SimServer>($"Updating SyncObject({syncObject})");
        
        byte[] array = new byte[data.Count];
        data.CopyTo(array);
        syncObject.Data = new ArraySegment<byte>(array);
        
        // Check if we've received the player config object yet
        var playerConfigType = NetObjectType.PlayerConfig;
        bool isKartingConfig = Platform == Platform.Karting && syncObject.Type == playerConfigType.KartingTypeId;
        bool isModNationConfig = Platform == Platform.ModNation && syncObject.Type == playerConfigType.ModnationTypeId;
        if (isModNationConfig || isKartingConfig)
            ParsePlayerConfig(data, player);
        
        return true;
    }

    private bool RemoveUserSyncObject(int guid, GamePlayer player)
    {
        SyncObject? syncObject = GetOwnedSyncObject(guid, player);
        if (syncObject == null) return false;
        
        Logger.LogDebug<SimServer>($"Removing SyncObject({syncObject})");
        _syncObjects.Remove(guid);
        return true;
    }

    private void TriggerRaceEventSync(EventUpdateReason reason, EventSettings? newEventSettings = null)
    {
        if (_raceSettings == null || Type != ServerType.Competitive) return;
        
        if (newEventSettings != null)
        {
            newEventSettings.UpdateReason = reason;
            _raceSettings.Value = newEventSettings;
        }
        else
        {
            _raceSettings.Value.UpdateReason = reason;
            _raceSettings.Sync();    
        }
        
        Logger.LogDebug<SimServer>($"Sending EventSettingsUpdate UpdateReason {reason}");
        
        _raceSettings.Value.UpdateReason = EventUpdateReason.None;
        _raceSettings.UpdateNoSync();
    }
    
    public void OnNetworkMessage(GamePlayer player, uint senderNameUid, NetMessageType type, ArraySegment<byte> data)
    {
        if (
            type != NetMessageType.VoipPacket && 
            type != NetMessageType.SpectatorInfo &&
            type != NetMessageType.MessageUnreliableBlock && 
            type != NetMessageType.MessageReliableBlock &&
            type != NetMessageType.WandererUpdate &&
            type != NetMessageType.GenericGameplay &&
            type != NetMessageType.WorldObjectStateChange
            )
        {
            Logger.LogTrace<SimServer>($"Received NetMessage {type} from {player.Username} ({(uint)player.UserId}:{player.State.NameUid}:{(uint)player.PlayerId})");   
        }
        
        // The name UID should match the one we've received from the player state update
        if (player.State.HasNameUid && senderNameUid != player.State.NameUid)
        {
            Logger.LogWarning<SimServer>($"NameUID for {player.Username} doesn't match as expected! Disconnecting! ({player.State.NameUid} != {senderNameUid}");
            player.Disconnect();
            return;
        }
        
        switch (type)
        {
            case NetMessageType.PlayerDetachGuestInfo:
            {
                NetMessagePlayerInfo message;
                try
                {
                    message = NetworkReader.Deserialize<NetMessagePlayerInfo>(data);
                }
                catch (Exception)
                {
                    Logger.LogWarning<SimServer>($"Failed to parse PlayerDetachGuestInfo for {player.Username}, disconnecting them from the session.");
                    player.Disconnect();
                    break;
                }

                foreach (var detachInfo in message.Data)
                {
                    int index = _playerInfos.FindIndex(info =>
                        info.PlayerName == detachInfo.PlayerName && info.NetcodeUserId == player.UserId);
                    if (index != -1)
                        _playerInfos.RemoveAt(index);
                }
                
                // Tell everybody else that we've detached the guest(s)
                Broadcast(data, type);
                
                // Recalculate the pod positions
                if (Type == ServerType.Pod) RecalculatePodPositions();
                
                break;
            }
            case NetMessageType.ItemCreate:
            case NetMessageType.ItemDestroy:
            case NetMessageType.ItemHitPlayer:
            case NetMessageType.ItemHitConfirm:
            case NetMessageType.SecondaryCollision:
            {
                // TODO: Item Validation 
                //  - Verify that the player actually has the item before
                //  - either destroying it or accepting a hit player message.
                
                // ItemHitConfirm gets sent by the player that's being hit, so
                // we don't have to verify that message.
                
                Broadcast(data, type, player);
                break;
            }
            case NetMessageType.ArbitratedItemCreateBlock:
            {
                NetMessageArbitratedItemBlock block;
                try
                {
                    block = NetMessageArbitratedItemBlock.ReadVersioned(data, Platform);
                }
                catch
                {
                    Logger.LogWarning<SimServer>($"Failed to parse ArbitratedItemCreateBlock for {player.Username}, disconnecting them from the session.");
                    player.Disconnect();
                    break;
                }
                
                var created = new List<int>(block.ItemIds.Count);
                foreach (var item in block.ItemIds)
                {
                    if (_arbitrationServer.Create(block.ItemType, block.PlayerNameUid, block.AcquireBehavior, item))
                        created.Add(item);
                }
                
                block.ItemIds = created;
                Broadcast(block, NetMessageType.ArbitratedItemCreateBlock);
                
                break;
            }
            case NetMessageType.ArbitratedItemDestroyBlock:
            {
                NetMessageArbitratedItemBlock block;
                try
                {
                    block = NetMessageArbitratedItemBlock.ReadVersioned(data, Platform);
                }
                catch
                {
                    Logger.LogWarning<SimServer>($"Failed to parse ArbitratedItemDestroyBlock for {player.Username}, disconnecting them from the session.");
                    player.Disconnect();
                    break;
                }
                
                var destroyed = new List<int>(block.ItemIds.Count);
                foreach (var item in block.ItemIds)
                {
                    if (_arbitrationServer.Destroy(item, block.PlayerNameUid))
                        destroyed.Add(item);
                }
                
                block.ItemIds = destroyed;
                Broadcast(block, NetMessageType.ArbitratedItemDestroyBlock);
                
                break;
            }
            case NetMessageType.ArbitratedItemAcquire:
            {
                NetMessageArbitratedItem item;
                try
                {
                    item = NetMessageArbitratedItem.ReadVersioned(data, Platform);
                }
                catch
                {
                    Logger.LogWarning<SimServer>($"Failed to parse ArbitratedItemAcquire for {player.Username}, disconnecting them from the session.");
                    player.Disconnect();
                    break;
                }
                
                var response = NetMessageType.ArbitratedItemAcquireFailed;
                if (_arbitrationServer.Acquire(item.ItemId, item.PlayerNameUid, item.Timeout))
                    response = NetMessageType.ArbitratedItemAcquire;
                Broadcast(data, response);
                break;
            }
            case NetMessageType.ArbitratedItemCreate:
            {
                NetMessageArbitratedItem item;
                try
                {
                    item = NetMessageArbitratedItem.ReadVersioned(data, Platform);
                }
                catch
                {
                    Logger.LogWarning<SimServer>($"Failed to parse ArbitratedItemCreate for {player.Username}, disconnecting them from the session.");
                    player.Disconnect();
                    break;
                }
                
                if (_arbitrationServer.Create(item.ItemType, item.PlayerNameUid, AcquireBehavior.SingleAcquire, item.ItemId))
                    Broadcast(data, type);
                break;
            }
            case NetMessageType.ArbitratedItemRelease:
            {
                NetMessageArbitratedItem item;
                try
                {
                    item = NetMessageArbitratedItem.ReadVersioned(data, Platform);
                }
                catch
                {
                    Logger.LogWarning<SimServer>($"Failed to parse ArbitratedItemRelease for {player.Username}, disconnecting them from the session.");
                    player.Disconnect();
                    break;
                }
                
                _arbitrationServer.Release(item.ItemId, item.PlayerNameUid);
                break;
            }
            case NetMessageType.ArbitratedItemDestroy:
            {
                NetMessageArbitratedItem item;
                try
                {
                    item = NetMessageArbitratedItem.ReadVersioned(data, Platform);
                }
                catch
                {
                    Logger.LogWarning<SimServer>($"Failed to parse ArbitratedItemDestroy for {player.Username}, disconnecting them from the session.");
                    player.Disconnect();
                    break;
                }
                
                if (_arbitrationServer.Destroy(item.ItemId, item.PlayerNameUid))
                    Broadcast(data, type);
                
                break;
            }
            case NetMessageType.PlayerCreateInfo:
            {
                NetMessagePlayerInfo message;
                try
                {
                    message = NetworkReader.Deserialize<NetMessagePlayerInfo>(data);
                }
                catch (Exception)
                {
                    Logger.LogWarning<SimServer>($"Failed to parse PlayerCreateInfo for {player.Username}, disconnecting them from the session.");
                    player.Disconnect();
                    break;
                }

                if (message.Data.Count != player.Guests.Count + 1)
                {
                    Logger.LogWarning<SimServer>($"PlayerCreateInfo from {player.Username} doesn't contain correct number of infos! Disconnecting them from the session.");
                    player.Disconnect();
                    break;
                }
                
                var info = message.Data[0];

                if (info.NetcodeUserId != player.UserId || info.NetcodeGamePlayerId != player.PlayerId)
                {
                    Logger.LogWarning<SimServer>($"PlayerCreateInfo from {player.Username} doesn't match their connection details! Disconnecting them from the session.");
                    player.Disconnect();
                    break;
                }
                
                // Player create info gets sent again when attaching guests, make sure we remove all our old infos
                _playerInfos.RemoveAll(x => x.NetcodeUserId == player.UserId);
                
                player.IsSpectator = !CanJoinAsRacer();
                PlayerJoinStatus status =
                    player.IsSpectator ? PlayerJoinStatus.SpectatorPending : PlayerJoinStatus.RacerPending;
                
                // The player create info gets sent to the server with an operation of type none,
                // send it to the other players telling them we're joining.
                info.Operation = status;
                
                // Make sure to track our player info
                player.Info = info;
                _playerInfos.Add(info);
                
                // Handle additional guest player infos attached
                for (int i = 1; i < message.Data.Count; ++i)
                {
                    var guestInfo = message.Data[i];
                    var guest = player.GetGuestByName(guestInfo.PlayerName);
                    if (guest == null || guestInfo.NetcodeUserId != player.UserId || guestInfo.NetcodeGamePlayerId != player.PlayerId)
                    {
                        Logger.LogWarning<SimServer>($"PlayerCreateInfo from {player.Username} contains invalid guest info! Disconnecting them from the session.");
                        player.Disconnect();
                        break;
                    }
                    
                    // Cache the name uid for the starting grid
                    guest.NameUid = CryptoHelper.StringHashU32(guestInfo.NameUid);
                    
                    guestInfo.Operation = status;
                    guest.Info = guestInfo;
                    _playerInfos.Add(guestInfo);
                }
                
                // Does the player need their status back? If we changed the status maybe, but we should probably
                // just send it back when their gameroom is ready.
                Broadcast(message, type, player);
                
                // Since we have the player info now, we can recalculate the positions in
                // pod, if relevant
                if (Type == ServerType.Pod) 
                    RecalculatePodPositions();
                
                break;
            }
            case NetMessageType.WorldObjectCreate:
            {
                Broadcast(data, type);
                break;
            }
            case NetMessageType.KickPlayerRequest:
            {
                // Make sure we're in a valid gameroom state
                if (Type != ServerType.Competitive || _raceSettings == null) break;
                // Only the current session leader can kick people
                if (player.UserId != Owner) break;

                NetMessagePlayerRequest request;
                try
                {
                    request = NetMessagePlayerRequest.ReadVersioned(data, Platform);
                }
                catch (Exception)
                {
                    Logger.LogWarning<SimServer>($"Failed to parse KickPlayerRequest from {player.Username}, disconnecting them from the session.");
                    player.Disconnect();
                    break;
                }

                GamePlayer? target = _players.FirstOrDefault(target => target.State.NameUid == request.Target);
                target?.Kick(DisconnectReason.LeaderKickRequest);
                break;
            }
            case NetMessageType.LeaderChangeRequest:
            {
                // Make sure we're in a valid gameroom state
                if (Type != ServerType.Competitive || _raceSettings == null) break;
                // Only the current session leader can change the host
                if (player.UserId != Owner) break;
                
                NetMessagePlayerRequest request;
                try
                {
                    request = NetMessagePlayerRequest.ReadVersioned(data, Platform);
                }
                catch (Exception)
                {
                    Logger.LogWarning<SimServer>($"Failed to parse LeaderChangeRequest from {player.Username}, disconnecting them from the session.");
                    player.Disconnect();
                    break;
                }
                
                GamePlayer? target = _players.FirstOrDefault(target => target.State.NameUid == request.Target);
                if (target != null)
                {
                    _raceSettings.Value.OwnerNetcodeUserId = target.UserId;
                    Owner = target.UserId;
                    TriggerRaceEventSync(EventUpdateReason.HostChanged);
                }
                
                break;
            }
            case NetMessageType.PlayerLeave:
            {
                NetMessagePlayerLeave message;
                try
                {
                    message = NetMessagePlayerLeave.ReadVersioned(data, Platform);
                }
                catch (Exception)
                {
                    Logger.LogWarning<SimServer>(
                        $"Failed to parse PlayerLeave message from {player.Username}, disconnecting them from the session.");
                    player.Disconnect();
                    break;
                }
                
                // We'll send the leave message when the player actually gets disconnected from the session
                player.LeaveReason = message.Reason;
                break;
            }
            case NetMessageType.GameroomReady:
            {
                player.State.Flags |= PlayerStateFlags.GameRoomReady;
                player.State.IsConnecting = false;
                
                // In Karting, you can receive GameroomReady messages in the Pod
                if (Type == ServerType.Competitive)
                {
                    // If the player took too long to load, switch them to spectator
                    if (!CanJoinAsRacer()) SwitchToSpectator(player);
                    else if (RaceState == RaceState.GameroomCountdown)
                        UpdateRaceSetup();   
                }
                
                BroadcastPlayerState();
                BroadcastSessionInfo();
                
                break;
            }
            case NetMessageType.GameroomStopTimer:
            {
                if (player.UserId != Owner || Type != ServerType.Competitive) break;
                
                // Make sure we're still within the threshold of being able to stop the timer
                var room = _gameroomState.Value;
                float timeRemaining = (room.LoadEventTime - TimeHelper.LocalTime);
                if (timeRemaining > _raceConstants.GameRoomTimerRacerLock)
                {
                    SetCurrentGameroomState(RoomState.Ready);
                }
                
                break;
            }
            case NetMessageType.PostRaceVoteForTrack:
            {
                if (Type != ServerType.Competitive) break;
                if (IsKarting)
                {
                    var votes = _votePackage.Value;
                    
                    // If we're not in a voting state, don't accept any votes
                    if (!votes.IsVoting) break;
                    
                    // Data must be 4 bytes since it's just the voted track id
                    if (data.Count != 4)
                    {
                        Logger.LogWarning<SimServer>($"Failed to parse PostRaceVoteForTrack from {player.Username}, disconnecting them from the session.");
                        player.Disconnect();
                        break;
                    }
                    
                    int trackId = 0;
                    trackId |= data[0] << 24;
                    trackId |= data[1] << 16;
                    trackId |= data[2] << 8;
                    trackId |= data[3] << 0;
                    
                    votes.CastVote(player.UserId, trackId);                    
                    _votePackage.Sync();
                }
                
                break;
            }
            case NetMessageType.RankedEventVeto:
            {
                if (_raceSettings == null || !IsRanked || Type != ServerType.Competitive ||
                    player.State.HasEventVetoed) break;
                
                player.State.HasEventVetoed = true;
                if (_players.All(p => p.State.HasEventVetoed))
                {
                    _gameroomState.Value.HasEventVetoOccured = true;
                    _gameroomState.Sync();
                    
                    EventSettings raceSettings;
                    if (_seriesInfo != null)
                    {
                        var series = Career.ModNation.GetRankedSeries(5, Owner);
                        _seriesInfo.Value = series;
                        raceSettings = series.Events.First();
                    }
                    else
                    {
                        raceSettings = Career.ModNation.GetRankedEvent(Owner, _raceSettings.Value.CreationId);
                    }
                    
                    TriggerRaceEventSync(EventUpdateReason.RaceSettingsVetoed, raceSettings);
                    Room.UpdateAttributes(raceSettings);
                }
                
                Broadcast((int)player.State.NameUid, NetMessageType.RankedEventVeto);
                break;
            }
            case NetMessageType.SpectatorInfo:
            {
                // Only the owner should be able to update the spectator info for the gameroom
                if (player.UserId != Owner || Type != ServerType.Competitive) break;
                
                try
                {
                    _spectatorInfo.Value = SpectatorInfo.ReadVersioned(data, Platform);
                }
                catch (Exception)
                {
                    Logger.LogWarning<SimServer>($"Failed to parse SpectatorInfo from {player.Username}, disconnecting them from the session.");
                    player.Disconnect();
                }
                
                break;
            }
            case NetMessageType.GameroomDownloadTracksComplete:
            {
                player.State.Flags |= PlayerStateFlags.DownloadedTracks;
                break;
            }
            case NetMessageType.GameroomDownloadTracksFailed:
            {
                player.Kick(DisconnectReason.TrackDownloadFailed);
                break;
            }
            case NetMessageType.ReadyForEventStart:
            {
                player.State.Flags |= PlayerStateFlags.ReadyForEvent;
                break;
            }
            case NetMessageType.ReadyForNisStart:
            {
                player.State.Flags |= PlayerStateFlags.ReadyForNis;
                break;
            }
            case NetMessageType.GameroomRequestStartEvent:
            {
                // Only the host should be allowed to start the event I'm fairly sure
                if (player.UserId != Owner || Type != ServerType.Competitive || RaceState != RaceState.Invalid) break;
                SetCurrentGameroomState(RoomState.DownloadingTracks);
                break;
            }
            case NetMessageType.EventResultsPreliminary:
            {
                if (_raceSettings == null || Type != ServerType.Competitive) break;
                
                // Just in case someone submits race results after we've already ended
                if (RaceState != RaceState.Racing && RaceState != RaceState.WaitingForRaceEnd) break;
                
                List<EventResult> results;
                try
                {
                    NetMessageEventResults message = NetMessageEventResults.ReadVersioned(data, Platform);
                    results = EventResult.Deserialize(message.ResultsXml);
                }
                catch (Exception)
                {
                    Logger.LogWarning<SimServer>($"Failed to parse EventResultsPreliminary for {player.Username}, disconnecting them from the session.");
                    player.Disconnect();
                    break;
                }
                
                // bool isValid = true;
                // foreach (var result in results)
                // {
                //     bool isMyAi = _aiInfo.Value.DataSet.Any(ai => ai.NameUid == result.OwnerUid && ai.OwnerName == player.Username);
                //     bool isMe = result.OwnerUid == player.State.NameUid;
                //     bool isGuest = player.Guests.Any(guest => guest.NameUid == result.OwnerUid);
                //
                //     isValid = (isMyAi || isMe || isGuest);
                //     if (!isValid) break;
                //     if (isMe)
                //     {
                //         player.HasFinishedRace = result.PercentComplete >= 1.0f;
                //         player.Score = result.EventScore;
                //     }
                // }
                //
                // if (!isValid)
                // {
                //     Logger.LogWarning<SimServer>($"{player.Username} tried to submit invalid event results. Disconnecting them from the session.");
                //     player.Disconnect();
                //     break;
                // }
                
                _eventResults.AddRange(results);
                player.HasSentRaceResults = true;
                
                break;
            }
            case NetMessageType.TextChatMsg:
            {
                try
                {
                    NetChatMessage message = NetChatMessage.LoadXml(data, Platform);
                    
                    
                    // Basic spoofing prevention, don't allow sender name mismatches
                    if (message.Sender != player.Username)
                    {
                        Logger.LogWarning<SimServer>($"TextChatMsg from {player.Username} has mis-matching sender name {message.Sender}, disconnecting them from the session.");
                        player.Disconnect();
                        break;
                    }
                }
                catch
                {
                    Logger.LogWarning<SimServer>($"Failed to parse TextChatMsg from {player.Username}, disconnecting them from the session.");
                    player.Disconnect();
                    break;
                }
                
                Broadcast(data, type, player);
                break;
            }

            case NetMessageType.InviteSessionJoinDataModnation:
            {
                // challenge nameuid
                // challengee nameuid
                // invite status 1
                Broadcast(data, type, player);
                break;
            }

            case NetMessageType.InviteChallengeMessageModnation:
            {
                // challenger nameuid
                // challengee nameuid
                Broadcast(data, type, player);
                break;
            }

            case NetMessageType.InviteRequestJoin:
            {
                // challenge nameuid
                // challengee nameuid
                // invite status
                Broadcast(data, type, player);
                break;
            }

            case NetMessageType.PlayerFinishedEvent:
            {
                if (Type != ServerType.Competitive) break;
                
                if (RaceState == RaceState.Racing)
                {
                    RaceState = RaceState.WaitingForRaceEnd;
                    
                    // The catch up timer is 30 seconds, but give an additional 5 
                    // seconds on the server to wait for event results
                    _raceStateEndTime = TimeHelper.LocalTime + 35_000;
                }
                
                Broadcast(data, type, player);
                break;
            }
            
            case NetMessageType.GroupLeaderMatchmakingStatus:
            case NetMessageType.GenericGameplay:
            case NetMessageType.WorldObjectStateChange:
            case NetMessageType.WandererUpdate:
            {
                Broadcast(data, type, player);
                break;
            }
            case NetMessageType.EventSettingsUpdate:
            {
                // Only the host should be allowed to update unranked event settings I'm fairly sure
                if (player.UserId != Owner || IsRanked || Type != ServerType.Competitive) break;

                EventSettings settings;
                try
                {
                    settings = EventSettings.ReadVersioned(data, Platform);
                }
                catch (Exception)
                {
                    Logger.LogWarning<SimServer>($"Failed to parse EventSettingsUpdate from {player.Username}, disconnecting them from the session.");
                    player.Disconnect();
                    break;
                }
                
                settings.IsRanked = IsRanked;
                
                // Can't limit to less people than we actually have
                if (settings.MaxHumans < _players.Count)
                    break;

                // We don't know if we're doing a top tracks race until the host sends the
                // initial event settings, switch to a series race here.
                if (settings.CareerEventIndex == CoiInfo.SPHERE_INDEX_TOP_TRACKS)
                {
                    IsSeries = true;
                    
                    var series = WebApiManager.GetTopTrackSeries(Owner, settings.KartParkHome);
                    _seriesInfo = CreateSystemSyncObject(series, NetObjectType.SeriesInfo);
                    settings = series.Events[0];
                }

                // Since we got new settings, we should update the room attributes for matchmaking
                Room.UpdateAttributes(settings);

                if (_raceSettings != null) TriggerRaceEventSync(EventUpdateReason.RaceSettingsChanged, settings);
                else _raceSettings = CreateSystemSyncObject(settings, NetObjectType.RaceSettings);
                
                break;
            }
            case NetMessageType.VoipPacket:
            {
                BroadcastVoipData(data, player);
                break;
            }
            case NetMessageType.MessageReliableBlock:
            {
                // 0x320 voice data
                // 0x60 byte meta block?
                    // 0x0 - int data_size
                    // 0x4 - int ???
                    // 0x8 - int netcode_id
                    // 0xc - int ???
                    // 0x10 - int player_id (?)
                    // 0x14 - sequence
                    
                // input is VOICE @ 16000Hz
                    
                BroadcastBlock(data, true, player);
                break;
            }
            case NetMessageType.MessageUnreliableBlock:
            {
                // The only unreliable message block I've seen get sent is just input data,
                // and it just gets broadcasted to all other players, should probably check for other conditions?
                // But it seems fine for now.
                BroadcastBlock(data, false, player);
                break;
            }
            
            case NetMessageType.PlayerStateUpdate:
            {
                PlayerState state;
                try
                {
                    NetMessagePlayerUpdate message = NetMessagePlayerUpdate.ReadVersioned(data, Platform);
                    state = message.Data.ElementAt(0);
                }
                catch (Exception)
                {
                    Logger.LogWarning<SimServer>($"Invalid PlayerStateUpdate received from {player.Username}, disconnecting them from the session.");
                    player.Disconnect();
                    break;
                }

                if (state.NameUid != senderNameUid)
                {
                    Logger.LogWarning<SimServer>($"PlayerStateUpdate NameUid doesn't match SenderNameUid! Disconnecting player!");
                    player.Disconnect();
                    break;
                }
                
                // If we're still connecting, then this is the first player state update before we're actually in-game,
                // so check if anybody else has our name UID first, this will only happen if someone is exploiting or if
                // somebody on RPCS3 doesn't have their PSID set
                if (!player.State.HasNameUid)
                {
                    if (_players.Any(p => p.State.NameUid == state.NameUid))
                    {
                        Logger.LogWarning<SimServer>($"Disconnecting {player.Username} from game since another user already has the same UID!");
                        player.Disconnect();
                        break;
                    }
                }

                // Patch our existing player state with the new message
                player.State.Update(state);

                // Check if player is away, if they are switch them to spectator so they do not join the race
                if (player.State.Away != 0)
                    SwitchToSpectator(player);
                else
                    SwitchToRacer(player);
                
                // If we're not in a gameroom, there's no GameroomReady event, so wait until we've received
                // the player config and the second player state update to finish our "connecting" process.
                if (Type != ServerType.Competitive)
                {
                    if (player.State is { IsConnecting: true, WaitingForPlayerConfig: false })
                    {
                        player.State.IsConnecting = false;
                        BroadcastSessionInfo();
                    }   
                }
                
                BroadcastPlayerState();
                
                break;
            }

            case NetMessageType.SyncObjectRemove:
            {
                NetMessageSyncObjectRemove message;
                try
                {
                    message = NetworkReader.Deserialize<NetMessageSyncObjectRemove>(data);
                }
                catch (Exception)
                {
                    Logger.LogWarning<SimServer>($"Failed to parse SyncObjectRemove message from {player.Username}, disconnecting them from the session.");
                    player.Disconnect();
                    break;
                }
                
                if (RemoveUserSyncObject(message.Guid, player))
                {
                    Broadcast(data, type, player);
                }
                break;
            }
            
            case NetMessageType.SyncObjectUpdate:
            {
                NetMessageSyncObjectUpdate message;
                try
                {
                    message = NetworkReader.Deserialize<NetMessageSyncObjectUpdate>(data);
                }
                catch (Exception)
                {
                    Logger.LogWarning<SimServer>($"Failed to parse SyncObjectUpdate message from {player.Username}, disconnecting them from the session.");
                    player.Disconnect();
                    break;
                }
                
                if (UpdateUserSyncObject(message.Guid, message.Data, player))
                {
                    Broadcast(data, type, player);
                }
                break;
            }
            
            case NetMessageType.SyncObjectCreate:
            {
                bool success = false;
                if (IsModNation)
                {
                    NetMessageSyncObject message;
                    try
                    {
                        message = NetworkReader.Deserialize<NetMessageSyncObject>(data);
                    }
                    catch (Exception)
                    {
                        Logger.LogWarning<SimServer>($"Failed to parse SyncObject message from {player.Username}, disconnecting them from the session.");
                        player.Disconnect();
                        break;
                    }
                    
                    switch (message.MessageType)
                    {
                        case NetObjectMessageType.Create:
                        {
                            success = PersistUserSyncedObject(new SyncObject(message, player.UserId), player);
                            break;
                        }
                        case NetObjectMessageType.Update:
                        {
                            success = UpdateUserSyncObject(message.Guid, message.Data, player);
                            break;
                        }
                        case NetObjectMessageType.Remove:
                        {
                            success = RemoveUserSyncObject(message.Guid, player);
                            break;
                        }
                    }
                }

                if (IsKarting)
                {
                    NetMessageSyncObjectCreate message;
                    try
                    {
                        message = NetworkReader.Deserialize<NetMessageSyncObjectCreate>(data);
                    }
                    catch (Exception)
                    {
                        Logger.LogWarning<SimServer>($"Failed to parse SyncObjectCreate message from {player.Username}, disconnecting them from the session.");
                        player.Disconnect();
                        break;
                    }
                    
                    success = PersistUserSyncedObject(new SyncObject(message, player.UserId), player);
                }

                // If the operation succeeded, send the sync object message to everyone else in the server.
                if (success)
                {
                    Broadcast(data, type, player);
                }
                
                break;
            }

            default:
            {
                Logger.LogWarning<SimServer>("Unhandled network message type: " + type);
                break;
            }
        }
    }

    private void FinalizeVote()
    {
        if (_raceSettings == null || Type != ServerType.Competitive) return;
        
        if (IsKarting && _votePackage.Value.IsVoting)
        {
            var votes = _votePackage.Value;
            int trackId = votes.FinishVote();
            _votePackage.Sync();
            
            Logger.LogDebug<SimServer>($"Vote finalized, winner is {trackId}!");
        }
    }

    private string FinalizeEventResults()
    {
        if (_raceSettings == null || Type != ServerType.Competitive || _eventResults.Count < 1) return string.Empty;
        
        RaceType mode = _raceSettings.Value.RaceType;
        
        if (IsKarting)
        {
            switch (_eventResults[0].ScoreSortField)
            {
                case "battleKills":
                    _eventResults.Sort((a, z) => z.BattleKills.CompareTo(a.BattleKills));
                    break;

                case "pointsScored":
                    _eventResults.Sort((a, z) => z.PointsScored.CompareTo(a.PointsScored));
                    break;

                default:
                case "raceTimeScore":
                    _eventResults.Sort((a, z) => a.EventScore.CompareTo(z.EventScore));
                    break;
            }
        }
        else _eventResults.Sort((a, z) => a.EventScore.CompareTo(z.EventScore));

        
        int rank = 0;
        List<PlayerEventStats> stats = [];
        foreach (EventResult result in _eventResults)
        {
            rank++;
            GamePlayer? player = _players.SingleOrDefault(x => x.State.NameUid == result.OwnerUid);
            if (player == null) continue;
            stats.Add(new PlayerEventStats
            {
                BestDrift = result.BestDrift,
                BestHangTime = result.BestHangTime,
                Finished = player.HasFinishedRace,
                PlayerConnectId = player.State.PlayerConnectId,
                Rank = rank,
                BestLapTime = result.BestEventSubScore,
                FinishTime = result.EventScore,
                PlaygroupSize = result.PlayerGroupId != 0 ? _eventResults.Count(match => match.PlayerGroupId == result.PlayerGroupId) : 1,
                Points = result.PointsScored
            });
        }

        string gameType = "";

        switch (mode)
        {
            case RaceType.Pure:
                gameType = IsModNation ? "ONLINE_PURE_RACE" : "RACE";
                break;

            case RaceType.Action:
                gameType = IsModNation ? "ONLINE_ACTION_RACE" : "RACE";
                break;

            case RaceType.Battle:
                gameType = "BATTLE";
                break;

            case RaceType.HotSeat:
                gameType = "ONLINE_HOT_SEAT_RACE";
                break;

            default:
                gameType = IsModNation ? "ONLINE_PURE_RACE" : "RACE";
                break;
        }

        BombdServer.Comms.NotifyEventFinished(_raceSettings.Value.CreationId, stats, IsModNation, gameType, IsRanked);
        
        string xml = EventResult.Serialize(_eventResults);
        _eventResults.Clear();

        Logger.LogDebug<SimServer>("Finishing event with XML:\n" + xml);

        return xml;
    }

    private string FinalizeSeriesResults()
    {
        var racers = _players.Where(p => !p.IsSpectator).OrderBy(p => p.Score).ToList();
        for (int i = 0; i < racers.Count; ++i)
        {
            racers[i].Points = RaceConstants.SeriesPoints[i];
            racers[i].TotalPoints += RaceConstants.SeriesPoints[i];
        }

        var results = racers.OrderByDescending(p => p.TotalPoints).Select((p, i) => new SeriesResult
        {
            OwnerName = p.Username,
            TotalPoints = p.TotalPoints,
            PointsEarned = p.Points,
            DidNotFinish = p.HasFinishedRace ? 0 : 1,
            Ranking = i + 1
        }).ToList();
        
        return SeriesResult.Serialize(results);
    }
    
    private void SimUpdate()
    {
        var room = _gameroomState.Value;
        
        // If the race hasn't started update the gameroom's state based on 
        // the current players in the lobby.
        if (_raceSettings != null && room.State < RoomState.RaceInProgress)
        {
            int numReadyPlayers =
                _players.Count(x => (x.State.Flags & PlayerStateFlags.GameRoomReady) != 0 && !x.IsSpectator);
            bool hasMinPlayers = numReadyPlayers >= _raceSettings.Value.MinHumans;

            // Karting, series races, and ranked races don't allow the "owner" to start the race
            bool isAutoStart = IsKarting || IsRanked || _seriesInfo != null;
            
            if (hasMinPlayers && room.State <= RoomState.WaitingMinPlayers)
                SetCurrentGameroomState(RoomState.Ready);
            else if (isAutoStart && room.State == RoomState.Ready)
                SetCurrentGameroomState(RoomState.DownloadingTracks);
            else if (!hasMinPlayers)
                SetCurrentGameroomState(RoomState.WaitingMinPlayers);
        }
        
        switch (room.State)
        {
            case RoomState.CountingDown:
            {
                // I think the countdown should only pause for connecting people in ranked races?
                // Since the countdown auto-starts after minimum players is reached?
                if (IsRanked)
                {
                    // In ranked events, we should pause the timer if someone is still trying to connect since the host
                    // doesn't have the option to back out of the timer
                    if (_players.Any(x => (x.State.Flags & PlayerStateFlags.GameRoomReady) == 0))
                    {
                        SetCurrentGameroomState(RoomState.CountingDownPaused);
                        break;
                    }
                }
                else
                {
                    // If anybody is still loading past the racer lock, switch them all to spectators
                    var loadingPlayers = _players.Where(x => (x.State.Flags & PlayerStateFlags.GameRoomReady) == 0).ToList();
                    if (loadingPlayers.Count != 0 && !CanJoinAsRacer())
                    {
                        foreach (GamePlayer racer in loadingPlayers)
                            SwitchToSpectator(racer);
                    }
                }
                
                // Wait until the timer has finished counting down, then broadcast to everyone that the race is in progress
                if (TimeHelper.LocalTime >= room.LoadEventTime)
                {
                    SetCurrentGameroomState(RoomState.RaceInProgress);
                }

                break;
            }
            case RoomState.CountingDownPaused:
            {
                if (_players.All(x => (x.State.Flags & PlayerStateFlags.GameRoomReady) != 0))
                {
                    SetCurrentGameroomState(RoomState.CountingDown);
                }
                
                break;
            }
            case RoomState.DownloadingTracks:
            {
                List<GamePlayer> racers = _players.Where(player => !player.IsSpectator).ToList();
                if (racers.All(x => (x.State.Flags & PlayerStateFlags.DownloadedTracks) != 0))
                    SetCurrentGameroomState(RoomState.CountingDown);
                break;
            }
            case RoomState.RaceInProgress:
            {
                switch (RaceState)
                {
                    case RaceState.LoadingIntoRace:
                    {
                        List<GamePlayer> racers = _players.Where(player => !player.IsSpectator).ToList();
                        if (racers.All(racer => (racer.State.Flags & PlayerStateFlags.ReadyForNis) != 0))
                        {
                            Broadcast(TimeHelper.LocalTime, NetMessageType.NisStart);
                            RaceState = RaceState.Nis;
                        }
                        
                        break;
                    }
                    case RaceState.Nis:
                    {
                        List<GamePlayer> racers = _players.Where(player => !player.IsSpectator).ToList();
                        if (racers.All(x => (x.State.Flags & PlayerStateFlags.ReadyForEvent) != 0))
                        {
                            int countdown = TimeHelper.LocalTime + _raceConstants.EventCountdownTime;
                            Broadcast(countdown, NetMessageType.EventStart);
                            RaceState = RaceState.Racing;
                            // Since the race has started, clear all the race load flags for next time
                            foreach (var racer in racers) 
                                racer.State.Flags &= ~PlayerStateFlags.AllFlags;
                        }
                        
                        break;
                    }
                    case RaceState.Racing:
                    case RaceState.WaitingForRaceEnd:
                    {
                        bool shouldSendResults = 
                            (RaceState == RaceState.WaitingForRaceEnd && TimeHelper.LocalTime >= _raceStateEndTime) ||
                            _players.All(player => player.IsSpectator || player.HasSentRaceResults);
 
                        if (shouldSendResults)
                        {
                            string results = FinalizeEventResults();
                            
                            _destination = Destination.GameRoom;
                            if (_raceSettings != null && _seriesInfo != null)
                            {
                                var events = _seriesInfo.Value.Events;
                                int nextSeriesIndex = _raceSettings.Value.SeriesEventIndex + 1;
                                if (nextSeriesIndex < events.Count)
                                {
                                    var nextEvent = events[nextSeriesIndex];
                                    _raceSettings.Value = nextEvent;
                                    Room.UpdateAttributes(nextEvent);
                                    _destination = Destination.NextSeriesRace;
                                } else _destination = IsRanked ? Destination.KartPark : Destination.GameRoom;
                            }
                            // Single xp races in ModNation just return back to the kart park
                            else if (IsRanked) _destination = Destination.KartPark;
                            Logger.LogDebug<SimServer>($"{Room.Game.GameName} race has been completed, destination is {_destination}");
                            
                            int postRaceDelay = _raceConstants.PostRaceTime;
                            RaceState = RaceState.PostRace;
                            _raceStateEndTime = TimeHelper.LocalTime + postRaceDelay;
                            var message = new NetMessageEventResults(Platform)
                            {
                                SenderNameUid = NetworkMessages.SimServerUid,
                                ResultsXml = results,
                                Destination = _seriesInfo != null ? Destination.PostSeries : _destination,
                                PostEventDelayTime = _raceStateEndTime,
                                PostEventScreenTime = postRaceDelay
                            };
                            
                            Broadcast(message, NetMessageType.EventResultsFinal);
                            if (_seriesInfo != null)
                            {
                                message.Destination = _destination;
                                message.ResultsXml = FinalizeSeriesResults();
                                Broadcast(message, NetMessageType.SeriesResults);
                            }
                            
                            if (IsKarting) UpdateVotePackage();

                            // Broadcast new seed for the next race
                            _seed = CryptoHelper.GetRandomSecret();
                            Broadcast(_seed, NetMessageType.RandomSeed);
                        }
                        
                        break;
                    }
                    case RaceState.PostRace:
                    {
                        // Destroy all the arbitrated items that were created
                        var block = new NetMessageArbitratedItemBlock(Platform);
                        foreach (ItemNode item in _arbitrationServer.Items)
                            block.ItemIds.Add(item.Uid);
                        _arbitrationServer.Items.Clear();
                        Broadcast(block, NetMessageType.ArbitratedItemDestroyBlock);
                        
                        // After the race has officially ended, reset the gameroom state
                        if (TimeHelper.LocalTime >= _raceStateEndTime)
                        {
                            List<GamePlayer> racers = _players.Where(player => !player.IsSpectator).ToList();
                            
                            // Destroy the AI
                            _aiInfo.Value = new AiInfo(Platform);
                            
                            // Clean up state variables for the next race
                            foreach (var racer in racers)
                                racer.SetupNextRaceState();

                            switch (_destination)
                            {
                                case Destination.GameRoom:
                                {
                                    Logger.LogDebug<SimServer>("Resetting GameRoom state for next race!");
                                    
                                    // Tell spectators that we're connecting back into the lobby
                                    foreach (var racer in racers)
                                    {
                                        racer.TotalPoints = 0;
                                        racer.State.IsConnecting = true;
                                    }
                                    
                                    BroadcastPlayerState();
                                    SetCurrentGameroomState(RoomState.None);
                                
                                    // Reset back to the first series race
                                    if (_seriesInfo != null && _raceSettings != null)
                                        _raceSettings.Value = _seriesInfo.Value.Events[0];
                                    
                                    // Reset the voting package
                                    if (IsKarting && Type == ServerType.Competitive)
                                    {
                                        _votePackage.Value.Reset();
                                        _votePackage.Sync();
                                    }
                                    
                                    break;
                                }
                                case Destination.Pod or Destination.KartPark:
                                    RaceState = RaceState.Invalid;
                                    Logger.LogDebug<SimServer>("Not resetting GameRoom state since we're returning to KartPark!");
                                    break;
                                case Destination.NextSeriesRace:
                                    Logger.LogDebug<SimServer>($"Starting next series event!");
                                    StartEvent();
                                    break;
                            }
                        }
                
                        // Finalize the results for Karting before the post race section ends
                        if ((uint)TimeHelper.LocalTime >= (uint)_raceStateEndTime - 1000u)
                            FinalizeVote();
                        break;
                    }
                }
                
                break;
            }
        }
    }

    public void Tick()
    {
        _arbitrationServer.Update();
        if (Type == ServerType.Competitive)
            SimUpdate();
    }
}