using System.Reflection;
using Bombd.Globals;
using Bombd.Helpers;
using Bombd.Logging;
using Bombd.Serialization;
using Bombd.Types.Network.Races;
using Bombd.Types.Story;

namespace Bombd.Types.Network.Objects;

public class EventSettings : INetworkReadable, INetworkWritable
{
    public int Version = 0xC;
    public readonly Platform Platform;
    
    // Shared
    public int CreationId = 288;
    public RaceType RaceType = RaceType.Action;
    public int OwnerNetcodeUserId = -1;
    public bool Private;
    public int SeriesEventIndex = -1;
    public int MinHumans = 4;
    public int MaxHumans = 12;
    public bool AiEnabled = true;
    public int CareerEventIndex = -1;
    public EventUpdateReason UpdateReason = EventUpdateReason.None;
    
    // Karting
    public int MaxPlayers = 12;
    public int NumHoard;
    public LevelType LevelType = LevelType.Cooperative;
    public ScoreboardType ScoreboardType = ScoreboardType.Time;
    public bool TeamEnabled;
    public Difficulty StoryDifficulty = Difficulty.Normal;
    
    // Modnation
    public bool IsRanked;
    public bool AutoReset = true;
    public string NisName = string.Empty;
    public string TranslatedTrackName = string.Empty;
    public SpeedClass KartSpeed = SpeedClass.Fastest;
    public int NumLaps = 3;
    public string KartParkHome = string.Empty;
    public string TrackName = "T1_ModCircuit";

    public EventSettings(Platform platform)
    {
        Platform = platform;
    }

    public EventSettings(Platform platform, CareerTrack track)
    {
        Platform = platform;
        TrackName = track.Name;
        CreationId = track.Id;
        KartSpeed = track.Class;
        RaceType = track.Type;
        MinHumans = track.MinPlayers;
        MaxHumans = track.MaxPlayers;
        ScoreboardType = track.ScoreboardType;
        LevelType = track.LevelType;
    }
    
    public static EventSettings ReadVersioned(ArraySegment<byte> data, Platform platform)
    {
        var settings = new EventSettings(platform);
        using var reader = NetworkReaderPool.Get(data);
        settings.Read(reader);
        return settings;
    }

    public void Print()
    {
        Logger.LogDebug<EventSettings>("EventSettingsUpdate");
        Type type = typeof(EventSettings);
        FieldInfo[] fields = type.GetFields();
        foreach (var field in fields)
            Logger.LogDebug<EventSettings>($"\t{field.Name} = {field.GetValue(this)}");
    }
    
    public void Read(NetworkReader reader)
    {
        if (Platform == Platform.ModNation)
        {
            TrackName = reader.ReadString(0x40);
            CreationId = reader.ReadInt32();
            reader.Offset += 4; // Always 1?
            reader.Offset += 4; // Always 1?
            NumLaps = reader.ReadInt32();
            KartSpeed = (SpeedClass)reader.ReadInt32();
            RaceType = (RaceType)reader.ReadInt32(); // 0x2bc
            reader.Offset += 4; // Always 0? // 0x2c0
            AiEnabled = reader.ReadInt32() == 1; // 0x2c4
            AutoReset = reader.ReadInt32() == 1; // 0x2c8
            OwnerNetcodeUserId = reader.ReadInt32(); // 0x2cc
            IsRanked = reader.ReadInt32() == 1; // 0x2d0
            Private = reader.ReadInt32() == 1; // 0x2d4
            CareerEventIndex = reader.ReadInt32(); // 0x2d8
            SeriesEventIndex = reader.ReadInt32(); // 0x2dc
            reader.Offset += 4; // 0x2dc
            MinHumans = reader.ReadInt32();
            MaxHumans = reader.ReadInt32();
            reader.Offset += 4;
            UpdateReason = (EventUpdateReason)reader.ReadInt32();
            KartParkHome = reader.ReadString(0x40);
            TranslatedTrackName = reader.ReadString(0x40);
            NisName = reader.ReadString(0x40); // intro (NIS) name which is always overwritten and sent out by the server, but keeping here for symmetry
            
            return;
        }

        Version = reader.ReadInt32();
        CreationId = reader.ReadInt32();
        MaxPlayers = reader.ReadInt32();
        RaceType = (RaceType)reader.ReadInt32();
        OwnerNetcodeUserId = reader.ReadInt32();
        Private = reader.ReadInt32() == 1;
        SeriesEventIndex = reader.ReadInt32();
        MinHumans = reader.ReadInt32();
        MaxHumans = reader.ReadInt32();
        NumHoard = reader.ReadInt32();
        AiEnabled = reader.ReadInt32() == 1;
        UpdateReason = (EventUpdateReason)reader.ReadInt32();
        CareerEventIndex = reader.ReadInt32();
        LevelType = (LevelType)reader.ReadInt32();
        ScoreboardType = (ScoreboardType)reader.ReadInt32();
        TeamEnabled = reader.ReadInt32() == 1;
        StoryDifficulty = (Difficulty)reader.ReadInt32();
    }

    public void Write(NetworkWriter writer)
    {
        if (Platform == Platform.ModNation)
        {   
            int start = writer.Offset; //temp
            writer.Write(TrackName, 0x40);
            writer.Write(CreationId); // 0x40
            writer.Write(1); // - PlayerCount or something? 0x48
            writer.Write(1); // - Unknown 0x48
            writer.Write(NumLaps); // 0x4c
            writer.Write((int)KartSpeed); // 0x50
            writer.Write((int)RaceType); // 0x54
            writer.Write(0); // - Unknown 0x58
            writer.Write(AiEnabled ? 1 : 0); // 0x5c
            writer.Write(AutoReset ? 1 : 0); // 0x60 - auto reset
            writer.Write(OwnerNetcodeUserId); // (0x64)

            writer.Write(IsRanked ? 1 : 0); // 0x68
            writer.Write(Private ? 1 : 0); // 0x6C
            writer.Write(CareerEventIndex); // 0x70
            writer.Write(SeriesEventIndex); // 0x74
            writer.Write(0); // 0x78
            writer.Write(MinHumans); // 0x7c
            writer.Write(MaxHumans); // 0x80
            writer.Write(0); // 0x84 - Padding?
            writer.Write((int)UpdateReason); // 0x88
        
            writer.Write(KartParkHome, 0x40); // 0x8c
            writer.Write(TranslatedTrackName, 0x40); // 0xcc
            Logger.LogInfo<EventSettings>($"Writing NIS '{NisName}' at offset 0x{writer.Offset - start:X}, track {CreationId}, reason {UpdateReason}, ranked {IsRanked}");
            writer.Write(NisName, 0x40); // 0x10c - intro (NIS) name which the server picks and sends to the clients
                                        // 0x14c end of settings

            return;
        }
        
        writer.Write(Version);
        writer.Write(CreationId);
        writer.Write(MaxPlayers);
        writer.Write((int)RaceType);
        writer.Write(OwnerNetcodeUserId);
        writer.Write(Private ? 1 : 0);
        writer.Write(SeriesEventIndex);
        writer.Write(MinHumans);
        writer.Write(MaxHumans);
        writer.Write(NumHoard);
        writer.Write(AiEnabled ? 1 : 0);
        writer.Write((int)UpdateReason);
        writer.Write(CareerEventIndex);
        writer.Write((int)LevelType);
        writer.Write((int)ScoreboardType);
        writer.Write(TeamEnabled ? 1 : 0);
        writer.Write((int)StoryDifficulty);
    }
    
}