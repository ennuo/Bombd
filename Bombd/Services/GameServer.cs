using System.Diagnostics.CodeAnalysis;
using Bombd.Attributes;
using Bombd.Core;
using Bombd.Helpers;
using Bombd.Logging;
using Bombd.Protocols;
using Bombd.Serialization;
using Bombd.Serialization.Wrappers;
using Bombd.Types.Events;
using Bombd.Types.Network;
using Bombd.Types.Network.Objects;
using Bombd.Types.Network.Room;
using Bombd.Types.Network.Simulation;
using Bombd.Types.Requests;
using Bombd.Types.Services;

namespace Bombd.Services;

[Service("gameserver", 50002, ProtocolType.RUDP)]
public class GameServer : BombdService
{
    private const int ReservationTimeout = 60_000;
    private const int JoinTimeout = 60_000;
    private const int MigrationTimeout = 60_000;
    
    private readonly Dictionary<string, ReservationGroup> _reservationGroups = new();
    private readonly List<PlayerJoinRequest> _playerJoinQueue = [];
    private readonly List<PlayerLeaveRequest> _playerLeaveQueue = [];
    private readonly object _playerLock = new();
    private readonly List<MigrationGroup> _playerMigrationGroups = [];
    public event EventHandler<PlayerJoinEventArgs>? OnPlayerJoined;
    public event EventHandler<PlayerLeaveEventArgs>? OnPlayerLeft;
    private volatile bool _wantResetHotLap;
    
    public void NotifyHotSeatReset()
    {
        // Going to handle the actual reset in the main loop to
        // avoid any issues with syncing.
        _wantResetHotLap = true;
    }
    
    public void UpdateGuestStatuses(GamePlayer player, GuestStatusBlock block)
    {
        lock (_playerLock)
        {
            player.Room.UpdateGuestStatuses(player, block);

            // Tell everybody else in the gameroom about any guests
            // that were either attached or detached
            var gamemanager = Bombd.GetService<GameManager>();
            foreach (GuestStatus guestStatus in block)
            {
                bool wasAttached = guestStatus.Status == GuestStatusCode.AttachSuccess;
                bool wasDetached = guestStatus.Status == GuestStatusCode.Detached;

                if (!wasAttached && !wasDetached) continue;

                var transaction = NetcodeTransaction.MakeRequest("gamemanager", wasAttached ? "guestJoined" : "guestLeft");
                transaction["gamename"] = player.Room.Game.GameName;
                transaction["playername"] = player.Username;
                transaction["guestname"] = guestStatus.Username;

                foreach (var peer in player.Room.Game.Players)
                {
                    if (peer == player) continue;
                    gamemanager.SendTransaction(peer.UserId, transaction);
                }
            }
        }
    }

    public bool ReserveSlotsInGame(string gameName, int numSlots, [MaybeNullWhen(false)] out string reservationKey)
    {
        reservationKey = null;
        lock (_playerLock)
        {
            GameRoom? room = Bombd.RoomManager.GetRoomByName(gameName);
            if (room == null) return false;
            if (room.NumFreeSlots < numSlots) return false;

            var slots = new Queue<int>();
            for (int i = 0; i < numSlots; ++i)
            {
                room.RequestSlot(out int slot);
                slots.Enqueue(slot);
            }

            reservationKey = CryptoHelper.GetReservationKey();
            _reservationGroups[reservationKey] = new ReservationGroup
            {
                Timestamp = TimeHelper.LocalTime,
                Slots = slots,
                Room = room
            };

            return true;
        }
    }

    public void AddMigrationGroup(GameMigrationRequest request)
    {
        lock (_playerLock)
        {
            // This shouldn't be normally possible, so we don't need to send back errors or anything, just return.
            GameRoom? currentRoom = Bombd.RoomManager.GetRoomByUser(request.HostUserId);
            if (currentRoom == null) return;

            var gamemanager = Bombd.GetService<GameManager>();
            bool isCreatingGame = string.IsNullOrEmpty(request.GameName);
            bool isJoiningGame = !isCreatingGame;

            GameRoom migratedRoom;
            if (isCreatingGame)
            {
                migratedRoom = Bombd.RoomManager.CreateRoom(new CreateGameRequest
                {
                    Attributes = request.Attributes!,
                    OwnerUserId = request.HostUserId,
                    Platform = request.Platform
                });
            }
            else
            {
                migratedRoom = Bombd.RoomManager.GetRoomByName(request.GameName);
                if (migratedRoom == null)
                {
                    var transaction = NetcodeTransaction.MakeRequest("gamemanager", "gameMigrationFailure");
                    transaction.Error = JoinFailReason.GameNotFound;
                    gamemanager.SendTransaction(request.HostUserId, transaction);
                    return;

                }
            }

            // Fairly sure with migration requests, guests are only ever attached when
            // starting a game from the co-op menu, so it should only ever be the "host's" single guest.
            string? guest = request.Guest;
            int numSlotsRequired = request.PlayerIdList.Count;
            if (guest != null) numSlotsRequired++;

            // If we're creating the game, we'll always have enough slots
            // So we have to make sure there are when joining
            if (isJoiningGame)
            {
                if (migratedRoom.NumFreeSlots < numSlotsRequired)
                {
                    var transaction = NetcodeTransaction.MakeRequest("gamemanager", "gameMigrationFailure");
                    transaction.Error = JoinFailReason.NotEnoughSlots;
                    gamemanager.SendTransaction(request.HostUserId, transaction);
                    return;
                }
            }

            var group = new MigrationGroup
            {
                OldRoom = currentRoom,
                NewRoom = migratedRoom,
                Timestamp = TimeHelper.LocalTime,
                Owner = request.HostUserId,
                OwnerGuest = request.Guest,
                Players = []
            };

            // Make sure we get a slot for the guest
            if (guest != null)
            {
                migratedRoom.RequestSlot(out int guestId);
                group.OwnerGuestId = guestId;
            }

            foreach (GenericInt32 playerId in request.PlayerIdList)
            {
                // If the player isn't in the game, just ignore them
                if (!currentRoom.IsPlayerInGame(playerId)) continue;

                GamePlayer player = currentRoom.GetPlayer(playerId);
                ConnectionBase connection = UserInfo[player.UserId];

                migratedRoom.RequestSlot(out int slot);
                group.Players.Add(new MigratingPlayer
                {
                    OldPlayerId = playerId,
                    NewPlayerId = slot,
                    Status = MigrationStatus.WaitingForDisconnect,
                    UserId = player.UserId
                });

                var transaction = NetcodeTransaction.MakeRequest("gamemanager", "requestDirectHostConnection");
                transaction["listenIP"] = BombdConfig.Instance.ExternalIP;
                transaction["listenPort"] = Bombd.GameServer.Port.ToString();
                transaction["hashSalt"] = connection.HashSalt.ToString();
                transaction["sessionId"] = connection.SessionId.ToString();
                gamemanager.SendTransaction(connection.UserId, transaction);
            }

            _playerMigrationGroups.Add(group);
        }
    }

    public void AddPlayerToLeaveQueue(int userId, string username, string reason)
    {
        lock (_playerLock)
        {
            _playerLeaveQueue.Add(new PlayerLeaveRequest
            {
                UserId = userId,
                Username = username,
                Reason = reason
            });
        }
    }

    public void AddPlayerToJoinQueue(PlayerJoinRequest request)
    {
        lock (_playerLock)
        {
            _playerJoinQueue.Add(request);
        }
    }

    private void HandlePlayerLeaveRequests()
    {
        lock (_playerLock)
        {
            foreach (PlayerLeaveRequest request in _playerLeaveQueue)
            {
                GamePlayer? player = Bombd.RoomManager.GetPlayerInRoom(request.UserId);
                if (player == null)
                {
                    Logger.LogWarning<GameServer>($"{request.Username} tried to leave a game, but they aren't in one!");
                    continue;
                }

                LeaveGameInternal(player, request.Reason);
            }

            _playerLeaveQueue.Clear();
        }
    }

    private void LeaveGameInternal(GamePlayer player, string reason)
    {
        string username = player.Username;
        int userId = player.UserId;
        var room = player.Room;
        
        if (Bombd.RoomManager.RequestLeaveCurrentRoom(userId))
        {
            Logger.LogInfo<GameServer>($"{username} left {room.Game.GameName}.");
            player.Room.Simulation.OnPlayerLeft(player, reason == DisconnectReason.Generic);
            OnPlayerLeft?.Invoke(this, new PlayerLeaveEventArgs
            {
                Room = room,
                Player = player,
                Reason = reason
            });
                
            if (room.IsEmpty)
            {
                Logger.LogInfo<GameServer>($"Destroying {room.Game.GameName} since all players have left!");
                Bombd.RoomManager.DestroyRoom(room);
            }
        }
        else
        {
            Logger.LogWarning<GameServer>(
                $"{username} tried to leave {room.Game.GameName}, but operation failed.");
        }
    }

    private void HandlePlayerJoinRequests()
    {
        lock (_playerLock)
        {
            for (int i = 0; i < _playerJoinQueue.Count; ++i)
            {
                PlayerJoinRequest request = _playerJoinQueue[i];
                if (TimeHelper.LocalTime > request.Timestamp + JoinTimeout)
                {
                    Logger.LogWarning<GameServer>(
                        "A player took too long to join and was disconnected!");
                    _playerJoinQueue.RemoveAt(i--);
                    if (UserInfo.TryGetValue(request.UserId, out ConnectionBase? pendingConnection))
                        pendingConnection.Disconnect();
                    continue;
                }

                // This generally shouldn't happen if someone isn't manually making requests,
                // but still make sure to handle these cases.
                GameRoom? gameRoom = Bombd.RoomManager.GetRoomByName(request.GameName);
                if (gameRoom == null)
                {
                    Logger.LogWarning<GameServer>(
                        "A player tried to join a game room, but the room doesn't exist.");
                    _playerJoinQueue.RemoveAt(i--);
                    if (UserInfo.TryGetValue(request.UserId, out ConnectionBase? pendingConnection))
                        pendingConnection.Disconnect();
                    continue;
                }

                // Wait until the game room is ready to join before letting the player in
                if (!gameRoom.IsReadyToJoin(request.UserId)) continue;

                // Karting doesn't use migrations and just switches games, so make sure we leave the old room.
                // TEMP: Wait until player has left?
                GamePlayer? existingPlayer = Bombd.RoomManager.GetPlayerInRoom(request.UserId);
                if (existingPlayer != null) LeaveGameInternal(existingPlayer, "gameMigration");

                if (UserInfo.TryGetValue(request.UserId, out ConnectionBase? connection))
                {
                    if (!connection.IsAuthenticated) continue;

                    GamePlayer? player;
                    if (request.ReservationKey != null)
                    {
                        // Make sure the reservation key actually exists
                        if (!_reservationGroups.TryGetValue(request.ReservationKey, out ReservationGroup group))
                        {
                            Logger.LogWarning<GameServer>(
                                "A player tried to join a game room with an invalid reservation key!");
                            _playerJoinQueue.RemoveAt(i--);
                            if (UserInfo.TryGetValue(request.UserId, out ConnectionBase? pendingConnection))
                                pendingConnection.Disconnect();
                            continue;
                        }

                        // Shouldn't happen, but if it does, make sure we don't mismatch reservations
                        if (group.Room != gameRoom)
                        {
                            Logger.LogWarning<GameServer>(
                                "A player tried to join a game room with a mismatched reservation key!");
                            _playerJoinQueue.RemoveAt(i--);
                            if (UserInfo.TryGetValue(request.UserId, out ConnectionBase? pendingConnection))
                                pendingConnection.Disconnect();
                            continue;
                        }

                        Logger.LogDebug<GameServer>($"{request.Username} is joining room using a reservation (guest={request.Guest})");

                        // Reservations in Karting are a bit wonky right now, and I can't really test it,
                        // so we'll need this check to prevent any exceptions
                        string? guest = request.Guest;
                        int numSlotsRequired = 1;
                        if (guest != null) numSlotsRequired++;
                        if (group.Slots.Count < numSlotsRequired)
                        {
                            Logger.LogWarning<GameServer>("A player tried to join a game room with a reservation that doesn't have enough slots!");
                            continue;
                        }
                        ;

                        int playerId = group.Slots.Dequeue();
                        if (guest == null)
                        {
                            player = Bombd.RoomManager.JoinRoom(connection.Username, connection.UserId, playerId,
                                group.Room);
                        }
                        else
                        {
                            int guestId = group.Slots.Dequeue();
                            player = Bombd.RoomManager.JoinRoomWithGuest(connection.Username, guest, connection.UserId, playerId,
                                guestId, group.Room);
                        }

                        if (group.Slots.Count == 0)
                        {
                            Logger.LogDebug<GameServer>($"Destroying reservation {request.ReservationKey} since all slots have been used!");
                            _reservationGroups.Remove(request.ReservationKey);
                        }
                    }
                    else player = Bombd.RoomManager.RequestJoinRoom(connection.Username, connection.UserId, gameRoom, request.Guest);

                    if (player != null)
                    {
                        // For convenience, we attach the send method directly to the game player object,
                        // so that no lookups have to be performed within the simulation server environment.
                        player.Send = (bytes, type) => SendMessage(player.UserId, bytes, type);
                        player.Disconnect = () => Disconnect(player.UserId);

                        Logger.LogInfo<GameServer>($"{player.Username} joined {gameRoom.Game.GameName}.");

                        // Make sure to tell both the simulation instance and anything subscribed to game events
                        // about the new player that joined.
                        gameRoom.Simulation.OnPlayerJoin(player);
                        OnPlayerJoined?.Invoke(this, new PlayerJoinEventArgs
                        {
                            Room = gameRoom,
                            Player = player,
                            WasMigration = false
                        });
                    }
                    else
                    {
                        Logger.LogWarning<GameServer>(
                            $"{connection.Username} tried to join {gameRoom.Game.GameName}, but operation failed.");
                        if (UserInfo.TryGetValue(request.UserId, out ConnectionBase? pendingConnection))
                            pendingConnection.Disconnect();
                    }

                    _playerJoinQueue.RemoveAt(i--);
                }
            }

        }
    }

    private void HandlePlayerMigrationRequests()
    {
        lock (_playerLock)
        {
            int time = TimeHelper.LocalTime;
            for (int i = 0; i < _playerMigrationGroups.Count; ++i)
            {
                MigrationGroup group = _playerMigrationGroups[i];
                foreach (MigratingPlayer player in group.Players)
                {
                    if (player.Status >= MigrationStatus.Migrated) continue;
                    if (time > group.Timestamp + MigrationTimeout)
                    {
                        player.Status = MigrationStatus.MigrationFailed;
                        continue;
                    }

                    if (!UserInfo.TryGetValue(player.UserId, out ConnectionBase? connection))
                    {
                        if (player.Status == MigrationStatus.WaitingForDisconnect)
                            player.Status = MigrationStatus.WaitingForConnect;

                        continue;
                    }

                    if (player.Status == MigrationStatus.WaitingForConnect && connection.IsAuthenticated)
                        player.Status = MigrationStatus.Migrated;
                }

                bool isMigrationComplete = group.Players.All(player => player.Status >= MigrationStatus.Migrated);
                if (!isMigrationComplete) continue;

                // Now that all users are connected to the gameserver, let's add them to the game room
                GameRoom room = group.NewRoom;
                foreach (MigratingPlayer player in group.Players)
                {
                    bool isOwner = player.UserId == group.Owner;
                    string? guest = isOwner ? group.OwnerGuest : null;

                    // In case the player closed their game during migration or if something else caused a disconnection.
                    if (!UserInfo.TryGetValue(player.UserId, out ConnectionBase? connection) || !connection.IsAuthenticated)
                    {
                        player.Status = MigrationStatus.MigrationFailed;
                        group.NewRoom.FreeSlot(player.NewPlayerId);
                        if (isOwner && guest != null)
                            group.NewRoom.FreeSlot(group.OwnerGuestId);
                        continue;
                    }

                    GamePlayer gamePlayer;
                    if (isOwner && guest != null)
                    {
                        gamePlayer = Bombd.RoomManager.JoinRoomWithGuest(connection.Username, guest, connection.UserId,
                            player.NewPlayerId, group.OwnerGuestId, group.NewRoom);
                    }
                    else
                    {
                        gamePlayer = Bombd.RoomManager.JoinRoom(connection.Username, connection.UserId,
                            player.NewPlayerId, group.NewRoom);
                    }

                    gamePlayer.Send = (bytes, type) => SendMessage(gamePlayer.UserId, bytes, type);
                    gamePlayer.Disconnect = () => Disconnect(player.UserId);
                    Logger.LogInfo<GameServer>($"{gamePlayer.Username} migrated to {room.Game.GameName}.");
                    room.Simulation.OnPlayerJoin(gamePlayer);
                    OnPlayerJoined?.Invoke(this, new PlayerJoinEventArgs
                    {
                        Room = room,
                        Player = gamePlayer,
                        WasMigration = true
                    });
                }

                List<GenericInt32> migratedPlayers = group.Players
                    .Where(player => player.Status == MigrationStatus.Migrated)
                    .Select(player => new GenericInt32(player.OldPlayerId)).ToList();

                List<GenericInt32> unmigratedPlayers = group.Players
                    .Where(player => player.Status == MigrationStatus.MigrationFailed)
                    .Select(player => new GenericInt32(player.OldPlayerId)).ToList();

                // Tell the old room about the migration that just occurred
                var gamemanager = Bombd.GetService<GameManager>();
                var transaction = NetcodeTransaction.MakeRequest("gamemanager", "gameMigrationOccured");
                transaction["numPlayersMigrated"] = migratedPlayers.Count.ToString();
                transaction["numPlayersNotMigrated"] = unmigratedPlayers.Count.ToString();
                transaction["playersNotMigrated"] = Convert.ToBase64String(NetworkWriter.Serialize(unmigratedPlayers));
                transaction["playersMigrated"] = Convert.ToBase64String(NetworkWriter.Serialize(migratedPlayers));
                foreach (GamePlayer player in group.OldRoom.Game.Players)
                    gamemanager.SendTransaction(player.UserId, transaction);

                // If any of the old players are still connected to the gamemanager
                // tell them that they failed to migrate
                foreach (MigratingPlayer player in group.Players)
                {
                    if (player.Status != MigrationStatus.MigrationFailed) continue;
                    transaction = NetcodeTransaction.MakeRequest("gamemanager", "gameMigrationFailure");
                    transaction.Error = "timeout";
                    SendTransaction(player.UserId, transaction);
                }

                _playerMigrationGroups.RemoveAt(i--);
            }
        }
    }

    private void ClearExpiredReservations()
    {
        lock (_playerLock)
        {
            int time = TimeHelper.LocalTime;
            foreach ((string? key, ReservationGroup group) in _reservationGroups.ToList())
            {
                if (time <= group.Timestamp + ReservationTimeout) continue;

                while (group.Slots.TryDequeue(out int slot))
                    group.Room.FreeSlot(slot);
                _reservationGroups.Remove(key);
            }
        }
    }

    protected override void OnGamedata(ConnectionBase connection, ArraySegment<byte> data)
    {
        // I think there should definitely be better session management storage.
        // But I'm still not entirely sure how to handle it. Seek feedback from others.
        GamePlayer? player = Bombd.RoomManager.GetPlayerInRoom(connection.UserId);
        if (player == null) return;
        
        using NetworkReaderPooled reader = NetworkReaderPool.Get(data);
        if (!NetworkMessages.Unpack(reader, connection.Platform, out NetMessageType type, out uint sender, out ArraySegment<byte> message))
        {
            Logger.LogError<GameServer>($"{player.Username} sent an invalid network message, disconnecting them from their current session!");
            player.Disconnect();
            return;
        }
        
        player.OnNetworkMessage(type, sender, message);
    }

    public override void OnTick()
    {
        var rooms = Bombd.RoomManager.GetRooms();
        foreach (GameRoom room in rooms)
        {
            // Don't bother updating game sessions that are empty
            if (room.Game.Players.Count == 0) continue;
            room.Simulation.Tick();
        }

        // Handling player join/leave requests after the main server operations so there's
        // no potential issue with concurrency.
        // e.g. someone joins mid tick
        HandlePlayerMigrationRequests();
        HandlePlayerLeaveRequests();
        HandlePlayerJoinRequests();
        ClearExpiredReservations();

        if (_wantResetHotLap)
        {
            Logger.LogInfo<GameServer>($"Got hot lap refresh message from PlayerConnect, re-syncing playlist to game rooms!");
            _wantResetHotLap = false;
            
            EventSettings settings = WebApiManager.GetHotSeat();
            foreach (GameRoom room in rooms)
                room.Simulation.OnHotSeatPlaylistRefresh(settings);
        }
    }

    public override void OnDisconnected(ConnectionBase connection)
    {
        UserInfo.TryRemove(connection.UserId, out _);

        if (Bombd.RoomManager.GetPlayerInRoom(connection.UserId) != null)
            AddPlayerToLeaveQueue(connection.UserId, connection.Username, DisconnectReason.Generic);

        Logger.LogInfo<GameServer>($"{connection.Username} has been disconnected.");
    }

    private enum MigrationStatus
    {
        WaitingForDisconnect,
        WaitingForConnect,
        Migrated,
        MigrationFailed
    }

    private class MigrationGroup
    {
        public int Timestamp;
        
        public int Owner;
        public string? OwnerGuest;
        public int OwnerGuestId;
        
        public required GameRoom OldRoom;
        public required GameRoom NewRoom;
        
        public List<MigratingPlayer> Players = [];
    }
    
    private struct ReservationGroup
    {
        public int Timestamp;
        public GameRoom Room;
        public Queue<int> Slots;
    }
    
    private class MigratingPlayer
    {
        public int OldPlayerId;
        public int NewPlayerId;
        public MigrationStatus Status;
        public int UserId;
    }
    
    private struct PlayerLeaveRequest
    {
        public int UserId;
        public string Username;
        public string Reason;
    }
}