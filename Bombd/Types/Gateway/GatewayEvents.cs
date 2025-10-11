namespace Bombd.Types.Gateway;

public static class GatewayEvents
{
    /// <summary>
    /// Send: Tells the Web API about this server
    /// </summary>
    public const string ServerInfo = "SERVER_INFO";
    
    /// <summary>
    /// Send: Reports that an event has started
    /// </summary>
    public const string EventStarted = "EVENT_STARTED";
    
    /// <summary>
    /// Send: Reports the results of a single event
    /// </summary>
    public const string EventFinished = "EVENT_FINISHED";
    
    /// <summary>
    /// Send: Reports that a player has updated their player data
    /// Primarily contains the character and kart id
    /// </summary>
    public const string PlayerUpdated = "PLAYER_UPDATED";
    
    /// <summary>
    /// Send: Reports that a player has quit an event before it has finished
    /// </summary>
    public const string PlayerQuit = "PLAYER_QUIT";
    
    /// <summary>
    /// Send: Reports the number of players connected
    /// </summary>
    public const string UpdatePlayerCount = "UPDATE_PLAYER_COUNT";
    
    /// <summary>
    /// Receive: A player has connected to the Web API
    /// </summary>
    public const string PlayerSessionCreated = "PLAYER_SESSION_CREATED";
    
    /// <summary>
    /// Receive: A player has been disconnected from the Web API.
    /// </summary>
    public const string PlayerSessionDestroyed = "PLAYER_SESSION_DESTROYED";
    
    /// <summary>
    /// Receive: New hot seat playlist has been set
    /// </summary>
    public const string HotSeatPlaylistReset = "HOT_SEAT_PLAYLIST_RESET";
}