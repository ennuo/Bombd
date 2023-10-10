using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.IO;

using BombServerEmu_MNR.Src.Log;
using BombServerEmu_MNR.Src.DataTypes;
using BombServerEmu_MNR.Src.Helpers;
using BombServerEmu_MNR.Src.Helpers.Extensions;

namespace BombServerEmu_MNR.Src.Protocols.Clients
{
    class SSLClient : IClient
    {
        public bool IsConnected
        {
            get { return Client.Connected && stream.CanRead && stream.CanWrite; }
        }
        public bool HasDirectConnection { get; set; }
        public string Username { get; set; }
        public int UserId { get; set; }
        public IPEndPoint RemoteEndPoint { get; }

        public BombService Service { get; }

        public TcpClient Client { get; }
        public X509Certificate2 Cert { get; }

        UniversalNetworkStream stream;

        Timer keepAlive;

        public SSLClient(BombService service, TcpClient client, X509Certificate2 cert = null)
        {
            Service = service;
            Client = client;
            Cert = cert;
            //SetKeepAlive(5000);
            if (cert == null)
            {
                stream = new UniversalNetworkStream(client.GetStream());
                return;
            }
            Logging.Log(typeof(SSLClient), "Attempting to get SSLStream...", LogType.Debug);
            RemoteEndPoint = (IPEndPoint)client.Client.RemoteEndPoint;
            var sslStream = new FixedSslStream(client.GetStream(), false);
            sslStream.AuthenticateAsServer(cert, false, SslProtocols.Ssl2 | SslProtocols.Ssl3 | SslProtocols.Tls, false);
            stream = new UniversalNetworkStream(sslStream);
            Logging.Log(typeof(SSLClient), "SSLStream OK!", LogType.Debug);
        }

        public void SetKeepAlive(int interval)
        {
            keepAlive = new Timer(SendKeepAlive, new AutoResetEvent(false), 0, interval);
            Logging.Log(typeof(SSLClient), "Updated KeepAlive interval to {0}ms", LogType.Debug, interval);
        }
        
        public void SendNetcodeData(BombXml xml)
        {
            WriteSocket(Encoding.ASCII.GetBytes(xml.GetResDoc()), EBombPacketType.ReliableNetcodeData);
        }
        
        public void SendReliableGameData(EndiannessAwareBinaryWriter bw)
        {
            WriteSocket(((MemoryStream)bw.BaseStream).ToArray(), EBombPacketType.ReliableGameData);
        }

        public void SendUnreliableGameData(EndiannessAwareBinaryWriter bw)
        {
            WriteSocket(((MemoryStream)bw.BaseStream).ToArray(), EBombPacketType.UnreliableGameData);
        }

        public void SendKeepAlive()
        {
            WriteSocket(new byte[0], EBombPacketType.KeepAlive);
        }
        void SendKeepAlive(object stateInfo) => SendKeepAlive();    //For timer

        public void SendReset()
        {
            WriteSocket(new byte[0], EBombPacketType.Reset);
        }

        public void SendAck(EBombPacketType protocol, int sequence)
        {
            WriteSocket(new byte[0], EBombPacketType.Acknowledge);
        }

        public void SendHandshake()
        {
            WriteSocket(new byte[0], EBombPacketType.Handshake);
        }

        public void Close()
        {
            if (keepAlive != null)
                keepAlive.Dispose();
            stream.Close();
            Client.Close();
        }

        public void UpdateOutgoingData()
        {
            
        }

        ~SSLClient()
        {
            Close();
        }


        public byte[] GetData(out EBombPacketType type)
        {
            var header = new byte[24];
            stream.Read(ref header, 0, header.Length);
            var len = ((header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3]) - 20;
            
            type = (EBombPacketType)header[20];
            
            // No point trying to read nothing
            if (len == 0) return null;
            
            var buf = new byte[len];
            var bytesRead = 0;
            do
            {
                bytesRead += stream.Read(ref buf, bytesRead, buf.Length - bytesRead);
                Logging.Log(typeof(SSLClient), "Read {0}/{1} bytes", LogType.Debug, bytesRead, buf.Length);
            } while (bytesRead < len);
            
            if (type == EBombPacketType.KeepAlive)
                Logging.Log(typeof(SSLClient), "KeepAlive received!", LogType.Debug);
            else if (type == EBombPacketType.ReliableNetcodeData)
                return buf.ToArray();
            return null;
        }
        
        void WriteSocket(byte[] data, EBombPacketType packetType)
        {
            using (var ms = new MemoryStream())
            using (var bw = new EndiannessAwareBinaryWriter(ms, EEndianness.Big))
            {
                bw.Write(data.Length + 21);
                bw.Write(new byte[16]);
                bw.Write((byte)packetType);    //Protocol type (TCP=0x64FEFFFF)
                bw.Write((byte)0xFE);
                bw.Write((byte)0xFF);
                bw.Write((byte)0xFF);
                bw.Write(data);
                bw.Write((byte)0);
                byte[] buf = ((MemoryStream)bw.BaseStream).ToArray();

                int bytesWritten = 0;
                do
                {
                    int toWrite = Math.Min(1024, buf.Length - bytesWritten);
                    stream.Write(ref buf, bytesWritten, toWrite);
                    bytesWritten += toWrite;
                    Logging.Log(typeof(SSLClient), "Wrote {0}/{1} bytes", LogType.Debug, bytesWritten, buf.Length);
                } while (bytesWritten < buf.Length);
            }
        }
    }
}
