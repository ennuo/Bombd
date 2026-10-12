using System.Collections.Concurrent;
using Bombd.Helpers;
using Bombd.Types.Events;
using Bombd.Types.GameBrowser;
using Bombd.Types.GameManager;
using Bombd.Types.Network;
using Bombd.Types.Network.Objects;
using Bombd.Types.Network.Races;
using Bombd.Types.Network.Simulation;
using Bombd.Types.Requests;

namespace Bombd.Core;

public class RoomManager
{
    public event EventHandler<GameEventArgs>? OnGameEvent;
    
    private readonly ConcurrentDictionary<string, GameRoom> _rooms = new();
    private readonly ConcurrentDictionary<int, GameRoom> _userRooms = new();
    
    private readonly Dictionary<int, int> _creationPlayerCounts = new();
    
    private readonly object _roomLock = new();
    private readonly object _playerCountLock = new();
    private int _nextGameId;
    
    public List<GameRoom> GetRooms()
    {
        lock (_roomLock)
        {
            return new List<GameRoom>(_rooms.Values);
        }
    }

    public GameRoom? GetRoomByUser(int userId)
    {
        return _userRooms.GetValueOrDefault(userId);
    }

    public GameRoom? GetRoomByName(string name)
    {
        return _rooms.GetValueOrDefault(name);
    }

    public GamePlayer? GetPlayerInRoom(int userId)
    {
        GameRoom? room = GetRoomByUser(userId);
        return room?.GetUser(userId);
    }

    public void DestroyRoom(GameRoom room)
    {
        lock (_roomLock)
        {
            _rooms.TryRemove(room.Game.GameName, out _);
        }
        
        OnGameEvent?.Invoke(this, new GameEventArgs(GameEventType.Shutdown, room));
    }
    
    public GamePlayer? RequestJoinRoom(string username, int userId, GameRoom room, string? guest)
    {
        GamePlayer? player = room.RequestJoin(username, userId, guest);
        if (player == null) return null;
        
        if (room.Platform == Platform.Karting)
            IncrementPlayerCount(room);
        
        _userRooms[player.UserId] = room;
        return player;
    }

    public GamePlayer JoinRoom(string username, int userId, int playerId, GameRoom room)
    {
        GamePlayer player = room.Join(username, userId, playerId);
        if (room.Platform == Platform.Karting)
            IncrementPlayerCount(room);
        _userRooms[player.UserId] = room;
        return player;
    }

    public GamePlayer JoinRoomWithGuest(string username, string guestName, int userId, int playerId, int guestId, GameRoom room)
    {
        GamePlayer player = room.JoinWithGuest(username, guestName, userId, playerId, guestId);
        if (room.Platform == Platform.Karting)
            IncrementPlayerCount(room);
        _userRooms[player.UserId] = room;
        return player;
    }
    
    public bool RequestLeaveCurrentRoom(int userId)
    {
        GameRoom? room = GetRoomByUser(userId);
        if (room == null) return false;
        
        if (room.Platform == Platform.Karting)
            DecrementPlayerCount(room);
        
        _userRooms.TryRemove(userId, out _);
        
        GamePlayer player = room.GetUser(userId);
        return room.Leave(player.PlayerId);
    }

    public GameRoom CreateRoom(CreateGameRequest request)
    {
        // Add internal attributes to the room
        request.Attributes["COMM_CHECKSUM"] = ((int)request.Platform).ToString();
        request.Attributes["__IS_RANKED"] = request.IsRanked ? "1" : "0";
        request.Attributes["__JOIN_MODE"] = "OPEN";
        request.Attributes["__MM_MODE_G"] = "OPEN";
        request.Attributes["__MM_MODE_P"] = "OPEN";
        request.Attributes["IS_LOCKED"] = "0";

        // Set default server type if none was provided, although this
        // generally shouldn't happen
        request.Attributes.TryAdd("SERVER_TYPE", "kartPark");
        
        var type = ServerType.KartPark;
        if (request.Attributes["SERVER_TYPE"] == "competitive")
            type = ServerType.Competitive;
        else if (request.Platform == Platform.Karting)
            type = ServerType.Pod;
        
        // The game generally doesn't tell us how many slots we need, so we'll just default to some value,
        // and then update it again when we receive the event settings from the host
        int maxPlayers = type switch
        {
            // The pod in LBPK should only be allowed to have 4 people when online
            ServerType.Pod => 4,
            // MNR official servers reports a max player count of 24
            ServerType.KartPark => 24,
            // Otherwise just default ot 8.
            _ => 8
        };
        
        request.Attributes["__MAX_PLAYERS"] = maxPlayers.ToString();
        
        bool isSeries = false;
        if (request.Attributes.TryGetValue("SERIES_TYPE", out string? seriesType))
            isSeries = seriesType == "series";
        
        bool isUserOwnedGame = type is ServerType.Competitive or ServerType.Pod;
        lock (_roomLock)
        {
            int id = ++_nextGameId;

            string name = request.Attributes["SERVER_TYPE"].ToLower();
            if (request.IsRanked) name = "ranked";
            name = $"gm_{name}_{id}{(uint)TimeHelper.LocalTime}";
            var room = new GameRoom(new RoomCreationInfo
            {
                Game = new GameManagerGame
                {
                    GameName = name,
                    GameBrowserName = name,
                    GameId = id,
                    Players = [],
                    Attributes = request.Attributes
                },
                Type = type,
                Platform = request.Platform,
                MaxPlayers = maxPlayers,
                OwnerUserId = isUserOwnedGame ? request.OwnerUserId : -1,
                IsRanked = request.IsRanked,
                IsSeries = isSeries
            });
        
            _rooms[name] = room;
            
            return room;
        }
    }
    
    public GameBrowserBusiestList GetBusiestCreations()
    {
        var busiest = new GameBrowserBusiestList();
        
        List<KeyValuePair<int, int>> creations;
        lock (_playerCountLock)
        {
            creations = _creationPlayerCounts.ToList();
        }
        
        creations.Sort((z, a) => a.Value.CompareTo(z.Value));
        foreach (var creation in creations)
        {
            if (busiest.Count == GameBrowserBusiestList.MaxSize) break;
            if (creation.Value == 0) continue;
            busiest.Add(creation.Key);
        }   
        
        return busiest;
    }

    public void FillCreationPlayerCounts(GamePlayerCounts counts)
    {
        lock (_playerCountLock)
        {
            foreach (int key in counts.Data.Keys.Where(key => _creationPlayerCounts.ContainsKey(key)))
            {
                counts.Data[key] = _creationPlayerCounts[key];
            }   
        }
    }

    private void IncrementPlayerCount(GameRoom room)
    {
        if (!room.Game.Attributes.TryGetValue("TRACK_CREATIONID", out string? attribute)) return;
        if (!int.TryParse(attribute, out int creationId)) return;
        lock (_playerCountLock)
        {
            if (!_creationPlayerCounts.TryAdd(creationId, 1))
                _creationPlayerCounts[creationId] += 1;
        }
    }
    
    private void DecrementPlayerCount(GameRoom room)
    {
        if (!room.Game.Attributes.TryGetValue("TRACK_CREATIONID", out string? attribute)) return;
        if (!int.TryParse(attribute, out int creationId)) return;
        lock (_playerCountLock)
        {
            if (_creationPlayerCounts.TryGetValue(creationId, out int playerCount))
                _creationPlayerCounts[creationId] = playerCount - 1;   
        }
    }

    public List<GameBrowserGame> GetKartParkSubMatches(GameRoom room)
    {
        return GetRooms().Where(s => s.KartParkHome == room).Select(s => s.GetGameBrowserInfo()).ToList();
    }
    
    public List<GameBrowserGame> SearchRooms(GameAttributes attributes, Platform id, int freeSlotsRequired, bool createIfNoneExist = true)
    {
        List<GameRoom> rooms = GetRooms().Where(room =>
        {
            // Don't bother checking anything if the room is full
            if (room.IsFull) return false;
            
            // Don't allow searching for other people's pod instances
            if (room.Simulation.Type == ServerType.Pod) return false;
            
            if (room.Platform != id) return false;
            if (room.NumFreeSlots < freeSlotsRequired) return false;
            
            // If the owner isn't in the game, the race either ended and the gameroom is shutting down
            // or they just haven't connected yet, so wait for that.
            if (!room.IsOwnerInGame()) return false;

            if (room.Simulation.Type == ServerType.Competitive)
            {
                // If it's a user hosted race, wait until the host has uploaded their race settings
                // before advertising the session.
                if (!room.Simulation.HasRaceSettings)
                    return false;
            }
            
            foreach (KeyValuePair<string, string> attribute in attributes)
            {
                if (attribute.Value == "_ANY") continue;
                if (!room.Game.Attributes.TryGetValue(attribute.Key, out string? value)) return false;
                if (value != attribute.Value)
                    return false;
            }

            return true;
        }).ToList();

        if (rooms.Count == 0 && createIfNoneExist)
            rooms.Add(CreateRoom(new CreateGameRequest { Attributes = attributes, Platform = id }));

        return rooms.Select(room => room.GetGameBrowserInfo()).ToList();
    }

    public void UpdateRoom(GameRoom room, EventSettings settings)
    {
        GameAttributes attr = room.Game.Attributes;
        lock (_roomLock)
        {
            string visibility = settings.Private ? "CLOSED" : "OPEN";
            attr["__MM_MODE_G"] = visibility;
            attr["__MM_MODE_P"] = visibility;
            attr["__JOIN_MODE"] = visibility;
            attr["IS_LOCKED"] = (room.Simulation.RaceState >= RaceState.LoadingIntoRace || !room.Simulation.CanJoinAsRacer()) ? "1" : "0";

            attr["__MAX_PLAYERS"] = settings.MaxHumans.ToString();
            
            // TODO: Adjust player counts on Karting?
            attr["TRACK_CREATIONID"] = settings.CreationId.ToString();
            if (settings.CreationId >= NetCreationIdRange.MinOnlineCreationId)
                attr["TRACK_GROUP"] = "userCreated";
            else attr["TRACK_GROUP"] = "official";
            
            attr.Remove("KART_PARK_HOME");
            attr.Remove("SPHERE_INDEX");
            if (room.Platform == Platform.ModNation)
            {
                if (!string.IsNullOrEmpty(settings.KartParkHome))
                {
                    attr["KART_PARK_HOME"] = settings.KartParkHome;
                    if (room.KartParkHome == null)
                    {
                        GameRoom? kartPark = GetRoomByName(settings.KartParkHome);
                        if (kartPark != null)
                        {
                            room.KartParkHome = kartPark;
                            OnGameEvent?.Invoke(this, new GameEventArgs(GameEventType.GameAddSubMatch, room));
                        }
                    }
                }
                else if (room.KartParkHome != null)
                {
                    OnGameEvent?.Invoke(this, new GameEventArgs(GameEventType.GameRemovedNoSubMatch, room));
                    room.KartParkHome = null;
                }
                
                if (settings.CareerEventIndex != -1)
                    attr["SPHERE_INDEX"] = settings.CareerEventIndex.ToString();
            }
        
            if (settings.SeriesEventIndex == -1)
            {
                attr["SERIES_TYPE"] = "single";
                attr.Remove("EVENT_INDEX");
            }
            else
            {
                attr["SERIES_TYPE"] = "series";
                attr["EVENT_INDEX"] = settings.SeriesEventIndex.ToString();
            }
        
            switch (settings.KartSpeed)
            {
                case SpeedClass.Fast:
                    attr["SPEED_CLASS"] = "fast";
                    break;
                case SpeedClass.Faster:
                    attr["SPEED_CLASS"] = "faster";
                    break;
                case SpeedClass.Fastest:
                    attr["SPEED_CLASS"] = "fastest";
                    break;
                default:
                    attr.Remove("SPEED_CLASS");
                    break;
            }

            switch (settings.RaceType)
            {
                case RaceType.Pure:
                    attr["MODE_TYPE"] = "pure";
                    break;
                case RaceType.Action:
                    attr["MODE_TYPE"] = "action";
                    break;
                case RaceType.Battle:
                    attr["MODE_TYPE"] = "battle";
                    break;
                default:
                    attr.Remove("MODE_TYPE");
                    break;
            }
            
            OnGameEvent?.Invoke(this, new GameEventArgs(GameEventType.UpdatedAttributes, room));
        }
    }
}