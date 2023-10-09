using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.IO;

using BombServerEmu_MNR.Src.Log;
using BombServerEmu_MNR.Src.DataTypes;
using BombServerEmu_MNR.Src.Helpers;
using BombServerEmu_MNR.Src.Helpers.Extensions;

namespace BombServerEmu_MNR.Src.Protocols.Clients
{
    class RUDPClient : IClient
    {
        const int MAX_PAYLOAD_SIZE = 1024;

        public bool IsConnected
        {
            get { return true; }    //TODO: Get connection state
        }
        public bool HasDirectConnection { get; set; }
        public IPEndPoint RemoteEndPoint { get; }

        public BombService Service { get; }

        public UdpClient Client { get; }
        public IPEndPoint EndPoint { get; }
        public Queue<byte[]> PacketQueue { get; } = new Queue<byte[]>();

        EBombPacketType lastPacketType;

        Timer keepAlive;

        public RUDPClient(BombService service, UdpClient listener, IPEndPoint endPoint)
        {
            Service = service;
            Client = listener;
            EndPoint = endPoint;
            RemoteEndPoint = (IPEndPoint)Client.Client.RemoteEndPoint;
            //Perform an acknowledge and sync to get the client to send its data
            SendAcknowledge();
            SendSync();
        }

        public void SetKeepAlive(int interval)
        {
            keepAlive = new Timer(SendKeepAlive, new AutoResetEvent(false), 0, interval);
            Logging.Log(typeof(RUDPClient), "Updated KeepAlive interval to {0}ms", LogType.Debug, interval);
        }

        public BombXml GetNetcodeData()
        {
            return new BombXml(Service, Encoding.ASCII.GetString(ReadSocket()));
        }

        public void SendNetcodeData(BombXml xml)
        {
            WriteSocket(Encoding.ASCII.GetBytes(xml.GetResDoc()), EBombPacketType.ReliableNetcodeData);
        }

        public byte[] GetRawData()
        {
            return ReadSocket();
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

        public void SendAcknowledge()
        {
            WriteSocket(new byte[0], EBombPacketType.Acknowledge);
        }

        public void SendSync()
        {
            WriteSocket(new byte[0], EBombPacketType.Handshake);
        }

        public void Close()
        {
            if (keepAlive != null)
                keepAlive.Dispose();
            //TODO: Stream
            Client.Close();
        }

        ~RUDPClient()
        {
            Close();
        }

        byte[] ReadSocket()
        {
            Block();
            var ep = new IPEndPoint(IPAddress.None, 0);
            using (var ms = new MemoryStream(PacketQueue.Dequeue()))
            using (var br = new EndiannessAwareBinaryReader(ms, EEndianness.Big))
            {
                var packetType = (EBombPacketType)br.ReadByte();
                switch (packetType)
                {
                    case EBombPacketType.Acknowledge:
                        {
                            Logging.Log(typeof(RUDPClient), "ReadSocket::Acknowledge: Unimplemented!", LogType.Error);
                            break;
                        }
                    case EBombPacketType.KeepAlive:
                        {
                            // TODO: Extend a KeepAlive timer
                            WriteSocket(new byte[0], EBombPacketType.Acknowledge);
                            break;
                        }
                    case EBombPacketType.ReliableNetcodeData:
                        {
                            //byte[] buf = new byte[MAX_PAYLOAD_SIZE];
                            //int bytesRead = 0;
                            //do
                            //{
                            //    bytesRead += stream.Read(ref buf, bytesRead, buf.Length - bytesRead);
                            //    Logging.Log(typeof(SSLClient), "Read {0}/{1} bytes", LogType.Debug, bytesRead, buf.Length);
                            //} while (bytesRead < len && Block());
                            //return buf.ToArray();
                            WriteSocket(new byte[0], EBombPacketType.Acknowledge);
                            break;
                        }
                    case EBombPacketType.ReliableGameData:
                        {
                            Logging.Log(typeof(RUDPClient), "ReadSocket::ReliableGameData: Unimplemented!", LogType.Error);
                            WriteSocket(new byte[0], EBombPacketType.Acknowledge);
                            break;
                        }
                    case EBombPacketType.UnreliableGameData:
                        {
                            Logging.Log(typeof(RUDPClient), "ReadSocket::UnreliableGameData: Unimplemented!", LogType.Error);
                            break;
                        }
                    case EBombPacketType.VoipData: //Never used
                    case EBombPacketType.Reset:  //??? how to handle this? Its never used though
                    case EBombPacketType.Handshake:
                        WriteSocket(new byte[0], EBombPacketType.Acknowledge);
                        break;
                }
                lastPacketType = packetType;
            }
            return null;
        }

        void WriteSocket(byte[] data, EBombPacketType packetType)
        {
            using (var ms = new MemoryStream())
            using (var bw = new EndiannessAwareBinaryWriter(ms, EEndianness.Big))
            {
                bw.Write((byte)packetType);
                switch (packetType)
                {
                    case EBombPacketType.Acknowledge:
                        // 0x10 in size
                        // char Protocol
                        // char AckingProtocol
                        // short pad
                        // uint AckingSequenceNumber
                        // uint LocalTime
                        // uint Pad2

                        bw.Write((byte)lastPacketType);
                        bw.Write(new byte[14]);
                        break;
                    case EBombPacketType.ReliableNetcodeData:
                        // 0x410 in size
                        // char Protocol
                        // char GroupCompleteFlag
                        // short GroupID
                        // uint SequenceNumber
                        // uint Checksum
                        // uint Pad2
                        // uchar Payload[1024]
                        Logging.Log(typeof(RUDPClient), "ReadSocket::NetcodeData: Unimplemented!", LogType.Error);
                        break;
                    case EBombPacketType.ReliableGameData:
                        // 0x410 in size
                        // char Protocol
                        // char Source
                        // char Destination
                        // char GroupCompleteFlag
                        // uint SequenceNumber
                        // short GroupId
                        // ushort GroupSizeBytes
                        // ushort Checksum
                        // short PayloadBytes
                        // uchar Payload[1024]
                        Logging.Log(typeof(RUDPClient), "ReadSocket::ReliableGameData: Unimplemented!", LogType.Error);
                        break;
                    case EBombPacketType.UnreliableGameData:
                        // 0x410 in size
                        // char Protocol
                        // char Source
                        // short Destination
                        // short PayloadBytes
                        // ushort Checksum
                        // uchar Payload[1032]
                        Logging.Log(typeof(RUDPClient), "ReadSocket::UnreliableGameData: Unimplemented!", LogType.Error);
                        break;
                    case EBombPacketType.VoipData:
                        // 0x410 in size
                        // char Protocol
                        // char Source
                        // short Destination
                        // uint SequenceNumber
                        // uint Pad1
                        // uint Pad2
                        // char Payload[1024]

                        Logging.Log(typeof(RUDPClient), "WriteSocket::VoipData: Unimplemented!", LogType.Error);
                        break;
                    case EBombPacketType.Handshake:
                        // 0x14 in size
                        // char Protocol
                        // char pad
                        // ushort Checksum
                        // uint SequenceNumber
                        // uint SessionId
                        // uint SecretNum
                        // uint GameDataSequenceNumber

                        Logging.Log(typeof(RUDPClient), "WriteSocket::Handshake: Unimplemented!", LogType.Error);
                        break;
                    case EBombPacketType.KeepAlive:
                        // 0x10 in size
                        // char Protocol
                        // char pad
                        // short pad1
                        // uint SequenceNumber
                        // uint LocalTime
                        // uint Pad2

                        Logging.Log(typeof(RUDPClient), "WriteSocket::KeepAlive: Unimplemented!", LogType.Error);
                        break;

                    case EBombPacketType.Reset:
                        Logging.Log(typeof(RUDPClient), "WriteSocket::Reset: Unimplemented!", LogType.Error);
                        Close();
                        break;
                }
                Client.Send(data, data.Length, EndPoint);
            }
        }

        bool Block()
        {
            while (PacketQueue.Count == 0)
                Thread.Sleep(10);   //Find a better way of blocking
            return true;
        }
    }
}
