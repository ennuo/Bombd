using Bombd.Core;
using Bombd.Helpers;
using Bombd.Logging;
using Bombd.Types.GameBrowser;
using Bombd.Types.GameManager;
using Bombd.Types.Network.Objects;
using Bombd.Types.Network.Races;
using Bombd.Types.Network.Room;
using Bombd.Types.Requests;

namespace Bombd.Types.Network.Simulation;

public class GameRoom
{
    private const int MaxSlots = 24;
    private const int InvalidSlot = -1;
    private const int SlotBitsMask = 0x1f;
    
    public readonly Platform Platform;
    
    public int TrackCreationId { get; private set; }
    public GameRoom? KartParkHome;
    public readonly GameManagerGame Game;
    public readonly SimServer Simulation;
    
    public bool IsEmpty => _usedSlots == 0;
    public bool IsFull => _usedSlots >= _maxPlayers;
    public int UsedSlots => _usedSlots;
    public int NumFreeSlots => Math.Max(0, _maxPlayers - _usedSlots);
    
    private readonly Dictionary<int, GamePlayer> _playerIdLookup = new();
    private readonly List<bool> _slots;
    private readonly List<int> _guests;
    private readonly Dictionary<int, GamePlayer> _userIdLookup = new();

    private int _usedSlots;
    private int _maxPlayers;
    private int _gameCreationTime = TimeHelper.LocalTime;
    private int _lastPlayerJoinTime = TimeHelper.LocalTime;
    
    public GameRoom(RoomCreationInfo request)
    {
        Game = request.Game;
        Platform = request.Platform;
        Simulation = new SimServer(
            request.Type, this, request.OwnerUserId, request.IsRanked, request.IsSeries);
        _slots = Enumerable.Repeat(false, MaxSlots).ToList();
        _guests = Enumerable.Repeat(InvalidSlot, MaxSlots).ToList();
        _maxPlayers = request.MaxPlayers;
        _usedSlots = 0;
    }
    
    public void UpdateAttributes(EventSettings settings)
    {
        _maxPlayers = settings.MaxHumans;
        TrackCreationId = settings.CreationId;
        BombdServer.Instance.RoomManager.UpdateRoom(this, settings);
    }
    
    public GamePlayer? RequestJoin(string username, int userId, string? guest = null)
    {
        int numSlots = 1;
        if (guest != null) numSlots++;
        
        if (NumFreeSlots < numSlots) return null;
        
        RequestSlot(out int playerId);
        GamePlayer player = Join(username, userId, playerId);
        if (guest != null)
        {
            RequestSlot(out int guestId);
            AttachGuest(player, guest, guestId);
        }
        
        return player;
    }

    public GamePlayer Join(string username, int userId, int playerId)
    {
        var player = new GamePlayer(this, username, userId, playerId);
        
        _playerIdLookup[playerId] = player;
        _userIdLookup[userId] = player;
        Game.Players.Add(player);
        _lastPlayerJoinTime = TimeHelper.LocalTime;

        return player;
    }
    
    public GamePlayer JoinWithGuest(string username, string guestName, int userId, int playerId, int guestId)
    {
        GamePlayer player = Join(username, userId, playerId);
        AttachGuest(player, guestName, guestId);
        return player;
    }
    
    private void AttachGuest(GamePlayer player, string username, int id)
    {
        player.Guests.Add(new GameGuest(player.Username, username));
        int slotId = player.PlayerId & SlotBitsMask;
        _guests[slotId] = id;
    }

    public bool Leave(int playerId)
    {
        if (FreeSlot(playerId))
        {
            GamePlayer player = _playerIdLookup[playerId];
            _userIdLookup.Remove(player.UserId);
            _playerIdLookup.Remove(playerId);
            Game.Players.Remove(player);

            int slotId = playerId & SlotBitsMask;
            FreeSlot(_guests[slotId]);
            _guests[slotId] = InvalidSlot;
            
            return true;
        }

        return false;
    }

    public void UpdateGuestStatuses(GamePlayer player, GuestStatusBlock block)
    {
        int slotId = player.PlayerId & SlotBitsMask;
        
        foreach (GuestStatus guestStatus in block)
        {
            GameGuest? guest = player.GetGuestByName(guestStatus.Username);
            switch (guestStatus.Status)
            {
                case GuestStatusCode.AttachRequest:
                {
                    // If we already have a guest in-game, reject the request
                    if (guest != null || _guests[slotId] != InvalidSlot || !RequestSlot(out int guestId))
                    {
                        guestStatus.Status = GuestStatusCode.AttachFail;
                        continue;
                    }
                    
                    _guests[slotId] = guestId;
                    player.Guests.Add(new GameGuest(player.Username, guestStatus.Username));
                    guestStatus.Status = GuestStatusCode.AttachSuccess;
                    break;
                }
                case GuestStatusCode.DetachRequest:
                {
                    // The guest should be in game if we've received a detach request,
                    // but check if its null anyway.
                    if (guest != null)
                    {
                        FreeSlot(_guests[slotId]);
                        _guests[slotId] = InvalidSlot;
                        player.Guests.Remove(guest);
                    }
                    
                    guestStatus.Status = GuestStatusCode.Detached;
                    break;   
                }
            }
        }
    }
    
    public bool RequestSlot(out int playerId)
    {
        playerId = InvalidSlot;
        if (NumFreeSlots == 0) return false;

        int index = _slots.FindIndex(x => !x);
        if (index == -1) return false;

        _lastPlayerJoinTime = TimeHelper.LocalTime;
        _slots[index] = true;
        _usedSlots++;
        
        playerId = index & SlotBitsMask;
        return true;
    }

    public bool FreeSlot(int slot)
    {
        if (slot == InvalidSlot) return false;
        int slotId = slot & SlotBitsMask;
        
        if (_slots[slotId])
        {
            _usedSlots--;
            _slots[slotId] = false;
            return true;
        }
        
        return false;
    }
    
    public bool IsPlayerInGame(int playerId) => _playerIdLookup.ContainsKey(playerId);
    public bool IsUserInGame(int userId) => _userIdLookup.ContainsKey(userId);
    public bool IsOwnerInGame()
    {
        int owner = Simulation.Owner;
        return owner == -1 || _userIdLookup.ContainsKey(owner);
    }

    public bool IsReadyToJoin(int userId)
    {
        int owner = Simulation.Owner;
        if (userId == owner || owner == -1) return true;
        return Simulation.IsRoomReady();
    }
    
    public GamePlayer GetUser(int userId) => _userIdLookup[userId];
    public GamePlayer GetPlayer(int playerId) => _playerIdLookup[playerId];

    public GameBrowserGame GetGameBrowserInfo() =>
        new()
        {
            Platform = Platform,
            TimeSinceLastPlayerJoined = TimeHelper.LocalTime - _lastPlayerJoinTime,
            Players = new GameBrowserPlayerList(Game.Players),
            GameName = Game.GameName,
            DisplayName = Game.GameName,
            Attributes = Game.Attributes,
            NumFreeSlots = NumFreeSlots
        };
}