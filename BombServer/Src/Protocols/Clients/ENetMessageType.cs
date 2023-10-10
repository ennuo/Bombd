namespace BombServerEmu_MNR.Src.Protocols.Clients
{
    public enum ENetMessageType : byte
    {
        ItemHitConfirm = 0xa,
        TeamMessage = 0xb,
        SecondaryCollision = 0xc,
        WandererUpdate = 0xd,
        ItemDestroy = 0x11,
        ItemHitPlayer = 0x13,
        ReadyForEventStart = 0x14,
        EventStart = 0x15,
        EventResultsPreliminary = 0x16,
        EventResultsFinal = 0x17,
        GenericGameplay = 0x18,
        PlayerStateUpdate = 0x19,
        ArbitratedItemCreate = 0x1a,
        ArbitratedItemDestroy = 0x1b,
        ArbitratedItemAcquire = 0x1c,
        ArbitratedItemRelease = 0x1d,
        ArbitratedItemDestroyBlock = 0x1e,
        EventSettingsUpdate = 0x1f,
        TextChatMsg = 0x20,
        SeriesResults = 0x21,
        GameroomReady = 0x25,
        LeaderChangeRequest = 0x27,
        KickPlayerRequest = 0x28,
        RankedEventVeto = 0x29,
        ArbitratedItemAcquireFailed = 0x2a,
        MessageUnreliableBlock = 0x2b,
        
        // Data starts at 0x8
            // uint NumDatas
            // each Data is 0xC bytes in size
                // uint unk
                // uint unk
                // uint unk
        PlayerSessionInfo = 0x2c,
        
        
        // Two types, local and remote
        // Remote (Server -> Client):
            // Data starts at 0x10
            // char OwnerName[32]
            // char DebugTag[32]
            // uint Type
            // uint GUID
        // Local (Client -> Server):
            // Data starts at 0x8
            // uint GUID
            // uint Type
            // char* Data
        SyncObjectCreate = 0x2d,
        
        GameroomStopTimer = 0x2e,
        PlayerFinishedEvent = 0x2f,
        SpectatorInfo = 0x30,
        VoipPacket = 0x33,
        
        // Data starts at 0x8
        // uint NumUpdates
        // each player state is 0x18 bytes in size
            //  uint NameUID
            //  uint PcId
            //  uint KartId
            //  uint CharacterId
            //  uint Away
            //  uint Mic
        BulkPlayerStateUpdate = 0x34,
        
        // Data starts at 0x8
        // uint Seed
        RandomSeed = 0x35,
        
        GameroomDownloadTracksComplete = 0x37,
        GameroomDownloadTracksFailed = 0x38,
        ReadyForNisStart = 0x39,
        NisStart = 0x3a,
        ArbitratedItemCreateBlock = 0x3b,
        GameroomRequestStartEvent = 0x3c,
        GameroomLeaderVeto = 0x3d,
        InviteRequestJoin = 0x3e,
        PlayerLeave = 0x3f,
        SyncObjectUpdate = 0x40,
        SyncObjectRemove = 0x41,
        EventRestart = 0x42,
        PlayerCreateInfo = 0x43,
        SeriesSettingsUpdate = 0x44,
        InviteInviteMessage = 0x45,
        InviteChallengeMessage = 0x46,
        InviteSessionJoinData = 0x47,
        GroupLeaderMatchmakingStatus = 0x48,
        PlayerDetachGuestInfo = 0x49,
        MessageReliableBlock = 0x4a,
        TeamSettingsUpdate = 0x4b,
        ArbitratedItemAcquireBlock = 0x4c,
        PostRaceVoteForTrack = 0x4d,
        WorldobjectCreate = 0x4e,
        CoopPostraceLeaderAction = 0x4f,
    }
}