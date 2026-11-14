namespace Bombd.Types.Network.Races;

public enum EventUpdateReason
{
    None,
    RaceSettingsChanged,
    HostChanged,
    HostVetoed,
    RaceSettingsVetoed,
    Unknown5,       // 5 - not seen in the captures yet
    RaceStarting    // original server sends a 6th state where a new NIS is picked, trying this
}