namespace BombServerEmu_MNR.Src.Protocols.Clients
{
    public enum ENetObjectType : uint
    {
        Unknown = 0,
        PlayerConfig = 1,
        RaceSettings = 2,
        SpectatorInfo = 3,
        AiInfo = 4,
        SeriesInfo = 5,
        GameroomState = 6,
        StartingGrid = 7,
        BigBlob = 8,
        PodConfig = 9,
        PlayerAvatar = 10
    }
}