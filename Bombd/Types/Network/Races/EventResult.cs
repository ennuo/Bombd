using System.Xml;
using System.Xml.Serialization;

namespace Bombd.Types.Network.Races;

[XmlRoot("EventResult")]
public class EventResult
{
    // This class is serialized without namespaces and multiple root elements
    // which is a pain by default.
    private static XmlSerializer _serializer = new(typeof(EventResult));
    private static XmlReaderSettings _xmlReaderSettings = new() { ConformanceLevel = ConformanceLevel.Fragment };
    private static XmlWriterSettings _xmlWriterSettings = new()
    {
        ConformanceLevel = ConformanceLevel.Fragment, 
        OmitXmlDeclaration = true,
        Indent = false,
        NamespaceHandling = NamespaceHandling.OmitDuplicates
    };
    private static XmlSerializerNamespaces _xmlNamespaces = new([XmlQualifiedName.Empty]);
    
    [XmlAttribute("ownerUID")] public uint OwnerUid;
    [XmlAttribute("bestDrift")] public float BestDrift;
    [XmlAttribute("scoreSortOrder")] public string ScoreSortOrder = "ascending";
    [XmlAnyAttribute] public XmlAttribute[]? UnknownAttributes; // Adding more logging
    [XmlAttribute("bestEventSubScore")] public float BestEventSubScore;
    [XmlAttribute("bestHangTime")] public float BestHangTime;
    [XmlAttribute("eventScore")] public float EventScore;
    [XmlAttribute("eventScoreSim")] public float EventScoreSimulation;
    [XmlAttribute("percentComplete")] public float PercentComplete;

    [XmlAttribute("playerGroupID")] public int PlayerGroupId;
    [XmlAttribute("individualRaceTime")] public float IndividualRaceTime;
    [XmlAttribute("playgroupRaceTime")] public float PlaygroupRaceTime;
    [XmlAttribute("battleKills")] public int BattleKills;
    [XmlAttribute("pointsScored")] public int PointsScored;
    [XmlAttribute("individualPointsScored")] public int IndividualPointsScored;
    [XmlAttribute("playgroupPointsScored")] public int PlaygroupPointsScored;
    [XmlAttribute("scoreSortField")] public string ScoreSortField = "raceTimeScore";

    public static List<EventResult> Deserialize(string xml)
    {
        using var stringReader = new StringReader(xml);
        using var reader = XmlReader.Create(stringReader, _xmlReaderSettings);
        var events = new List<EventResult>();
        while (reader.MoveToContent() == XmlNodeType.Element)
        {
            events.Add((EventResult)_serializer.Deserialize(reader)!);
        }
        return events;
    }
    
    public static string Serialize(List<EventResult> results)
    {
        using var stream = new StringWriter();
        stream.Write("<?xml version=\"1.0\"?>\n");
        using var writer = XmlWriter.Create(stream, _xmlWriterSettings);
        foreach (EventResult result in results)
        {
            writer.WriteRaw("\n");
            _serializer.Serialize(writer, result, _xmlNamespaces);  
        }
        stream.Write("\0");
        return stream.ToString();
    }
}