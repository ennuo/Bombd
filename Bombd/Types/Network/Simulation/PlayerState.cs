using System.Text;
using System.Xml.Serialization;
using Bombd.Types.Network.Room;
using System.Xml;

namespace Bombd.Types.Network.Simulation;

[XmlRoot("PlayerState")]
public class PlayerState
{
    private static readonly XmlSerializer StateSerializer = new(typeof(PlayerState));

    [XmlIgnore] public int NetcodeUserId;
    [XmlIgnore] public bool HasEventVetoed;
    [XmlIgnore] public bool HasLeaderVetoed;
    [XmlIgnore] public bool IsConnecting = true;
    [XmlIgnore] public bool WaitingForPlayerConfig = true;
    [XmlIgnore] public int Flags = PlayerStateFlags.None;
    
    [XmlAttribute("nameUID")] public uint NameUid;
    [XmlAttribute("pcId")] public int PlayerConnectId;
    [XmlAttribute("kartId")] public int KartId;
    [XmlAttribute("characterId")] public int CharacterId;
    [XmlAttribute("away")] public int Away;
    [XmlAttribute("mic")] public int Mic;
    [XmlAttribute("styleDrift")] public float KartHandlingDrift;
    [XmlAttribute("styleAccel")] public float KartSpeedAccel;
    [XmlAnyAttribute] public XmlAttribute[]? UnknownAttributes; //adding some logging to see what clients send and dont hear back from the server

    public bool HasNameUid => NameUid != 0;
    
    public static PlayerState LoadXml(ArraySegment<byte> data)
    {
        int len = 0;
        while (len < data.Count && data[len] != 0)
            len++;
        
        string xml = len == 0 ? string.Empty : Encoding.ASCII.GetString(data.Array!, data.Offset, len);
        using var reader = new StringReader(xml);
        return (PlayerState) StateSerializer.Deserialize(reader)!;
    }

    public void Update(PlayerState state)
    {
        NameUid = state.NameUid;
        PlayerConnectId = state.PlayerConnectId;
        KartId = state.KartId;
        CharacterId = state.CharacterId;
        Away = state.Away;
        Mic = state.Mic;
        KartHandlingDrift = state.KartHandlingDrift;
        KartSpeedAccel = state.KartSpeedAccel;

        if (state.UnknownAttributes != null)
            UnknownAttributes = state.UnknownAttributes;
    }
    
    public override string ToString()
    {
        return
            $"PlayerConnect = {PlayerConnectId}, NameUID = {NameUid}, CharacterID = {CharacterId}, KartID = {KartId}, Away = {Away}, Connecting = {IsConnecting}, Mic = {Mic}, HasEventVetoed = {HasEventVetoed}, HasLeaderVetoed = {HasLeaderVetoed}";
    }
}