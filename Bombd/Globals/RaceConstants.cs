namespace Bombd.Globals;

public class RaceConstants
{
    public static readonly int[] SeriesPoints = [50, 40, 30, 20, 15, 10, 1, 1, 1, 1, 1, 1];
    
    public static readonly RaceConstants ModNation = new()
    {
        EventCountdownTime = 5000,
        GameRoomCountdownTime = 25000,
        GameRoomTimerRacerLock = 25000,
        GameRoomTimerLock = 20000,
        PostRaceTime = 15000
    };

    public static readonly RaceConstants Ranked = new()
    {
        EventCountdownTime = 5000,
        GameRoomCountdownTime = 60000,
        GameRoomTimerRacerLock = 25000,
        GameRoomTimerLock = 20000,
        PostRaceTime = 20000,
        SeriesPostRaceTime = 40000 // based on video evidence, series waited 40 seconds between each transition 
    };
    
    public static readonly RaceConstants Karting = new()
    {
        EventCountdownTime = 5000,
        GameRoomCountdownTime = 60000,
        GameRoomTimerRacerLock = 5000,
        GameRoomTimerLock = 5000,
        PostRaceTime = 45000
    };
    
    /// <summary>
    ///     How long the 3...2...1... timer lasts
    /// </summary>
    public int EventCountdownTime { get; set; } = 5000;
    
    /// <summary>
    ///     How long the countdown timer in rooms last in milliseconds
    /// </summary>
    public int GameRoomCountdownTime { get; set; }
        
    /// <summary>
    ///     The countdown time where players can no longer join the game in milliseconds
    /// </summary>
    public float GameRoomTimerRacerLock { get; set; } = 25000.0f;
        
    /// <summary>
    ///     The countdown time left before players can no longer join in milliseconds
    /// </summary>
    public float GameRoomTimerLock { get; set; }
        
        
    /// <summary>
    ///     How long the post-race scoreboard and voting lasts in milliseconds
    /// </summary>
    public int PostRaceTime { get; set; }

    /// <summary>
    ///     How long to wait between races in a series, in milliseconds (0 = use PostRaceTime)
    /// </summary>
    public int SeriesPostRaceTime { get; set; }
}