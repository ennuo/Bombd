using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Bombd.Core;
using Bombd.Helpers;
using Bombd.Logging;
using Bombd.Serialization;

namespace Bombd.Types.Network;

[XmlRoot("NetChatMessage")]
public class NetChatMessage : INetworkWritable
{
    private static readonly XmlSerializer StateSerializer = new(typeof(NetChatMessage));

    [XmlIgnore] public Platform Platform;
    [XmlAttribute("senderName")] public string Sender;
    [XmlAttribute("recipientName")] public string Recipient;
    [XmlAttribute("body")] public string Body;
    [XmlAttribute("isPrivate")] public int Private;
    
    public static NetChatMessage LoadXml(ArraySegment<byte> data, Platform platform)
    {
        string xml;
        if (platform == Platform.Karting)
        {
            int len = 0;
            len |= (data[0] << 24);
            len |= (data[1] << 16);
            len |= (data[2] << 8);
            len |= (data[3] << 0);

            if (data.Count != 4 + len)
                throw new ArgumentException("NetChatMessage packet is malformed!");

            xml = Encoding.ASCII.GetString(data.Array!, data.Offset + 4, len - 1);
        }
        else
        {
            int len = 0;
            while (len < data.Count && data[len] != 0)
                len++;

            xml = len == 0 ? string.Empty : Encoding.ASCII.GetString(data.Array!, data.Offset, len);
        }

        using var reader = new StringReader(xml);
        NetChatMessage message = (NetChatMessage)StateSerializer.Deserialize(reader)!;
        message.Platform = platform;
        return message;
    }

    public void Write(NetworkWriter writer)
    {
        MemoryStream stream = new();
        StateSerializer.Serialize(stream, this, new XmlSerializerNamespaces([XmlQualifiedName.Empty]));

        if (Platform == Platform.Karting)
        {
            stream.WriteByte(1);
            writer.Write((int)stream.Length+1);
        }

        stream.WriteByte(0);
        writer.Write(stream.ToArray());
    }
}