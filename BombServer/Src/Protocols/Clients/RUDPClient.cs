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
using BombServerEmu_MNR.Src.Services;

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
        
        private readonly Dictionary<ushort, byte[]> _dataFragments = new Dictionary<ushort, byte[]>();
        
        private int _seqNumber;
        private int _gameDataSeqNumber;
        private int _sessionId;
        private int _secret;

        private ushort _nextGroupId;
        
        private bool _hasCompletedHandshake;
        private Timer _keepAlive;

        public RUDPClient(BombService service, UdpClient listener, IPEndPoint endPoint)
        {
            Service = service;
            Client = listener;
            EndPoint = endPoint;
        }

        public void SetKeepAlive(int interval)
        {
            
        }
        
        public void SendNetcodeData(BombXml xml)
        {
            var data = Encoding.ASCII.GetBytes(xml.GetResDoc() + "\0");
            var offset = 0;
            var groupId = _nextGroupId++;
            do
            {
                var payloadSize = Math.Min(data.Length - offset, MAX_PAYLOAD_SIZE);
                var packetSize = 0x10 + payloadSize;
                
                using (var ms = new MemoryStream(packetSize))
                using (var bw = new EndiannessAwareBinaryWriter(ms, EEndianness.Big))
                {
                    uint checksum = 0;
                    
                    bw.Write((byte) EBombPacketType.ReliableNetcodeData);
                    bw.Write(offset + payloadSize >= data.Length);
                    bw.Write(groupId);
                    bw.Write(_seqNumber++);
                    bw.Write(checksum);
                    bw.Write(new byte[4]);
                    bw.Write(data, offset, payloadSize);
                    bw.Flush();
                    
                    var packet = ms.ToArray();
                    checksum = BombHMAC.GetMD532(packet, 0);
                    packet[8] = (byte)((checksum >> 24) & 0xff);
                    packet[9] = (byte)((checksum >> 16) & 0xff);
                    packet[10] = (byte)((checksum >> 8) & 0xff);
                    packet[11] = (byte)(checksum & 0xff);

                    Client.Send(packet, packetSize, EndPoint);
                }

                offset += payloadSize;
                
            } while (offset < data.Length);
        }

        public void SendReliableGameData(EndiannessAwareBinaryWriter bw)
        {
            // WriteSocket(((MemoryStream)bw.BaseStream).ToArray(), EBombPacketType.ReliableGameData);
        }

        public void SendUnreliableGameData(EndiannessAwareBinaryWriter bw)
        {
            // WriteSocket(((MemoryStream)bw.BaseStream).ToArray(), EBombPacketType.UnreliableGameData);
        }

        public void SendKeepAlive()
        {
            const int keepAlivePacketSize = 0x10;
            using (var ms = new MemoryStream(keepAlivePacketSize))
            using (var bw = new EndiannessAwareBinaryWriter(ms, EEndianness.Big))
            {
                bw.Write((byte) EBombPacketType.KeepAlive);
                bw.Write(new byte[3]);
                bw.Write(_seqNumber++);

                var localTime = (uint)(DateTime.UtcNow.Ticks / 10000);
                bw.Write(localTime);
                bw.Write(new byte[4]);
                
                bw.Flush();

                Client.Send(ms.ToArray(), keepAlivePacketSize, EndPoint);
                
            }
        }

        public void SendReset()
        {
            // WriteSocket(new byte[0], EBombPacketType.Reset);
        }
        
        public void SendAck(EBombPacketType protocol, int sequence)
        {
            const int ackPacketSize = 0x10;
            using (var ms = new MemoryStream(ackPacketSize))
            using (var bw = new EndiannessAwareBinaryWriter(ms, EEndianness.Big))
            {
                bw.Write((byte) EBombPacketType.Acknowledge);
                bw.Write((byte) protocol);
                bw.Write(new byte[2]);
                bw.Write(sequence);

                var localTime = (uint)(DateTime.UtcNow.Ticks / 10000);
                bw.Write(localTime);
                bw.Write(new byte[4]);
                
                bw.Flush();

                Client.Send(ms.ToArray(), ackPacketSize, EndPoint);
                
            }
        }

        public void SendHandshake()
        {
            const int handshakePacketSize = 0x14;
            using (var ms = new MemoryStream(handshakePacketSize))
            using (var bw = new EndiannessAwareBinaryWriter(ms, EEndianness.Big))
            {
                ushort checksum = 0;
                
                bw.Write((byte) EBombPacketType.Handshake);
                bw.Write((byte)0);
                bw.Write(checksum);
                
                bw.Write(_seqNumber++);
                bw.Write(_sessionId);
                bw.Write(_secret);
                // I don't know if we actually have to increment this here or not
                bw.Write(_gameDataSeqNumber++); 
                
                bw.Flush();

                var packet = ms.ToArray();
                checksum = BombHMAC.GetMD516(packet, 0);
                packet[2] = (byte)(checksum >> 8);
                packet[3] = (byte)(checksum & 0xff);

                Client.Send(packet, handshakePacketSize, EndPoint);
            }
        }

        public void Close()
        {
            if (_keepAlive != null)
                _keepAlive.Dispose();
            //TODO: Stream
        }

        public void UpdateOutgoingData()
        {
            // TODO: Keep track of lost packets and re-send if necessary here
        }

        ~RUDPClient()
        {
            // TODO: Remove this connection from RUDP when its unused
            // Close();
        }

        public byte[] GetData(out EBombPacketType type)
        {
            Block();
            var ep = new IPEndPoint(IPAddress.None, 0);
            using (var ms = new MemoryStream(PacketQueue.Dequeue()))
            using (var br = new EndiannessAwareBinaryReader(ms, EEndianness.Big))
            {
                type = (EBombPacketType)br.ReadByte();
                
                // If the packet is reliable, send back an acknowledgement
                if (type != EBombPacketType.Reset && type != EBombPacketType.Acknowledge &&
                    type != EBombPacketType.UnreliableGameData)
                {
                    br.BaseStream.Position += 3;
                    var sequence = br.ReadInt32();
                    
                    // For ReliableNetcodeData and ReliableGameData, do we have to send the acks only
                    // after the group is complete?
                    SendAck(type, sequence);
                    
                    // Undo position change in case we need to process some of the data later
                    br.BaseStream.Position -= 7;
                }
                
                switch (type)
                {
                    case EBombPacketType.Acknowledge:
                        // TODO: Cache packets in memory until acknowledge is received for sequence number to make protocol "reliable"
                        Logging.Log(typeof(RUDPClient), "ReadSocket::Acknowledge: Unimplemented!", LogType.Error);
                        break;
                    case EBombPacketType.KeepAlive: 
                        // TODO: Extend a KeepAlive timer
                        break;
                    case EBombPacketType.ReliableNetcodeData:
                    {
                        var groupCompleteFlag = br.ReadByte();
                        var groupId = br.ReadUInt16();
                        
                        // If the data is ever sent out of order, would you have to
                        // sort the data by the sequence number?
                        var sequenceNumber = br.ReadInt32();

                        var checksum = br.ReadInt32();
                        br.BaseStream.Position += 4;

                        // Is this just set to one if it's the last part of the data
                        // or are there some other flags?
                        var isLastFragment = (groupCompleteFlag & 1) != 0;
                        var data = br.ReadBytes((int)(br.BaseStream.Length - br.BaseStream.Position));
                        
                        if (_dataFragments.TryGetValue(groupId, out var group))
                        {
                            // Maybe I should use a list and merge them all when the last fragment is received?
                            var concat = new byte[data.Length + group.Length];
                            Buffer.BlockCopy(group, 0, concat, 0, group.Length);
                            Buffer.BlockCopy(data, 0, concat, group.Length, data.Length);

                            if (isLastFragment)
                            {
                                _dataFragments.Remove(groupId);
                                return concat;
                            }

                            _dataFragments[groupId] = concat;
                            break;
                        }

                        if (isLastFragment) return data;
                        _dataFragments[groupId] = data;
                        break;   
                    }
                    case EBombPacketType.ReliableGameData:
                        Logging.Log(typeof(RUDPClient), "ReadSocket::ReliableGameData: Unimplemented!", LogType.Error);
                        break;
                    case EBombPacketType.UnreliableGameData:
                        Logging.Log(typeof(RUDPClient), "ReadSocket::UnreliableGameData: Unimplemented!", LogType.Error);
                        break;
                    case EBombPacketType.VoipData: //Never used
                        break;
                    case EBombPacketType.Reset: //??? how to handle this? Its never used though
                        break;
                    case EBombPacketType.Handshake:
                    {
                        br.BaseStream.Position += 1;
                
                        // Should I bother verifying the checksum?
                        var checksum = br.ReadUInt16();
                
                        var sequenceNumber = br.ReadInt32();
                        _sessionId = br.ReadInt32();
                        _secret = br.ReadInt32();
                        var gameDataSequenceNumber = br.ReadInt32();
                        
                        SendHandshake();
                        _hasCompletedHandshake = true;
                        
                        break;   
                    }
                }
            }
            
            return null;
        }
        
        bool Block()
        {
            while (PacketQueue.Count == 0)
                Thread.Sleep(10);   //Find a better way of blocking
            return true;
        }
    }
}
