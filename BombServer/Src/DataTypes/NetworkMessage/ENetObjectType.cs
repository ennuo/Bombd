namespace BombServerEmu_MNR.Src.Protocols.Clients
{
    public enum ENetObjectType : uint
    {
        Unknown = 0,
        PlayerConfig = 1,
        RaceSettings = 2,
        SpectatorInfo = 3,
        AiInfo = 4,
        
        
        
        // 0x1f98 bytes
        // char [0x210][2] - Probably the data for the two monitors
        
        NetCoiInfoPackage = 5,
        SeriesInfo = 6,
        GameroomState = 7,
        StartingGrid = 8,
        BigBlob = 9,
        
        // NetcoiInfoPackage was removed in Karting
        // So all subsequent values are reduced by 1
        // SeriesInfo = 5,
        // GameroomState = 6,
        // StartingGrid = 7,
        // BigBlob = 8,
        // PodConfig = 9,
        // PlayerAvatar = 10
    }
}