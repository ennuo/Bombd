using System.Net;
using Bombd.Core;
using Bombd.Extensions;
using Bombd.Helpers;
using Bombd.Logging;
using Bombd.Serialization;
using Bombd.Types.Network;
using Bombd.Types.Services;

namespace Bombd.Protocols.RUDP;

public class RudpConnection : ConnectionBase
{
    private const int MinPacketSize = 8;
    private const int MaxPacketSize = 1040;
    private const int MaxPayloadSize = 1024;

    private const int VoipDataSize = 896;
    
    // Using a higher timeout because Karting at least doesn't
    // seem to send keep alive packets while the game is loading a level,
    // so disconnections are common with a smaller timeout.
    private const int PacketTimeout = 30_000;

    private const int MaxNetcodeSize = 0xFFFF;
    private const int MaxGamedataSize = 0x7FFFFF;

    // Maximum number of packets that can be sent but not acknowledged by one user
    private const int MaxInFlight = 32;

    private const int InitialResendTime = 300;
    private const int MinResendTime = 200;
    private const int MaxResendTime = 3000;

    private const int MaxBackoff = 4;

    private const int MaxReorderAhead = 64;

    private const bool EarlyAcks = false; // test to see if the game can handle True, this would allow certain packets to "skip" the line

    private const int DiagnosticsInterval = 2000; // buffer so logs don't get spammed

    
    private readonly List<RudpAckRecord> _ackList = new(16);

    // Start off with ModNation's packet size limit of ~65kbs,
    // the gamedata message handler will resize the buffer if necessary.
    private byte[] _groupBuffer = new byte[0x10000]; 
    
    private readonly RudpMessagePool _messagePool = new(32);

    private readonly List<RudpMessage> _sendBuffer = new(16);
    private readonly List<RudpMessage> _pendingSend = new(16);

    private readonly Dictionary<uint, byte[]> _earlyGamedata = new();
    private readonly Dictionary<uint, byte[]> _earlyNetcode = new();

    private readonly RudpServer _server;
    private int _groupOffset;

    private uint _lastReceiveTime;
    private uint _localGamedataSequence;
    private ushort _localGroupNumber;
    private ushort _remoteGroupNumber;
    
    private uint _localSequence;
    private uint _remoteGamedataSequence;
    private uint _remoteSequence;
    private int _secret;

    private float _smoothedRtt;
    private float _rttVariance;
    private bool _hasRttSample;
    private int _resendTimeout = InitialResendTime;
    private int _backoff;

    private uint _lastDiagnosticsLog;
    private int _resendsSinceLog;
    private int _reorderedSinceLog;
    private int _spuriousSinceLog;
    
    public RudpConnection(EndPoint endpoint, BombdService service, RudpServer server) : base(service, server)
    {
        _server = server;
        Endpoint = endpoint;
        State = ConnectionState.WaitingForHandshake;
    }

    public EndPoint Endpoint { get; }

    private bool VerifyChecksum16(ArraySegment<byte> data, int checksumOffset)
    {
        ushort csum = 0;
        csum |= (ushort)(data[checksumOffset + 0] << 8);
        csum |= (ushort)(data[checksumOffset + 1] << 0);

        data[checksumOffset + 0] = 0;
        data[checksumOffset + 1] = 0;

        return CryptoHelper.GetMD516(data, HashSalt) == csum;
    }

    private bool VerifyChecksum32(ArraySegment<byte> data, int checksumOffset)
    {
        int csum = 0;
        csum |= data[checksumOffset + 0] << 24;
        csum |= data[checksumOffset + 1] << 16;
        csum |= data[checksumOffset + 2] << 8;
        csum |= data[checksumOffset + 3] << 0;
        
        data[checksumOffset + 0] = 0;
        data[checksumOffset + 1] = 0;
        data[checksumOffset + 2] = 0;
        data[checksumOffset + 3] = 0;
        
        return CryptoHelper.GetMD532(data, HashSalt) == csum;
    }
    
    private bool VerifyPacket(ArraySegment<byte> data)
    {
        if (data.Count is < MinPacketSize or > MaxPacketSize) return false;
        
        var protocol = (PacketType)data[0];
        if (protocol is < PacketType.Reset or > PacketType.VoipData) return false;
        
        switch (protocol)
        {
            case PacketType.Reset:
            {
                return data.Count == 20;
            }
            case PacketType.Ack:
            case PacketType.KeepAlive:
            {
                return data.Count == 16;
            }
            case PacketType.Handshake:
            {
                // We'll verify the checksum later, we need to actually pull the session id first
                return data.Count == 20;
            }
            case PacketType.ReliableNetcodeData:
            {
                return data.Count >= 16 && VerifyChecksum32(data, 8);
            }
            case PacketType.UnreliableGameData:
            {
                int payloadBytes = (data[0x4] << 8) | data[0x5];
                return 8 + payloadBytes == data.Count && VerifyChecksum16(data, 6);
            }
            case PacketType.ReliableGameData:
            {
                if (data.Count < 16) return false;
                int payloadBytes = (data[0xE] << 8) | data[0xF];
                return 16 + payloadBytes == data.Count && VerifyChecksum16(data, 12);
            }
            case PacketType.VoipData: return data.Count == (16 + VoipDataSize);
        }
        
        return false;
    }

    internal void OnData(ArraySegment<byte> data)
    {
        if (State == ConnectionState.Disconnected) return;
        _lastReceiveTime = (uint)TimeHelper.LocalTime;

        if (!VerifyPacket(data))
        {
            Logger.LogInfo<RudpConnection>("Got invalid packet. Dropping connection.");
            Disconnect();
            return;
        }

        int offset = data.Offset;
        byte[] buffer = data.Array!;
        var protocol = (PacketType)buffer[offset++];

        if (protocol == PacketType.Reset)
        {
            Logger.LogInfo<RudpConnection>("Received client disconnect. Disconnecting.");

            // The service only needs to know if *authenticated* clients disconnected.
            // So only if we passed the waiting for connection state.
            if (State > ConnectionState.WaitingForConnection) Service.OnDisconnected(this);

            State = ConnectionState.Disconnected;
            _server.ConnectionsToRemove.Add(Endpoint);

            return;
        }

        if (State == ConnectionState.WaitingForHandshake)
        {
            HandleHandshake(data, buffer, offset, protocol);
            return;
        }
        
        if (protocol == PacketType.UnreliableGameData)
        {
            ProcessPacket(data);
            return;
        }

        buffer.ReadUint32BE(offset + 3, out uint sequence);

        if (protocol == PacketType.Ack)
        {
            HandleAck((PacketType)buffer[offset], sequence);
            return;
        }

        // We only need to acknowledge KeepAlive packets, so just stop here.
        // keepalive doesn't follow a normal sequence number, it's just whatever the current timestamp is.
        if (protocol == PacketType.KeepAlive)
        {
            QueueAck(protocol, sequence);
            return;
        }

        if (protocol is PacketType.ReliableGameData or PacketType.ReliableNetcodeData)
        {
            ReceiveReliable(data, protocol, sequence);
            return;
        }

        if (sequence <= _remoteSequence) QueueAck(protocol, sequence);
        if (sequence != _remoteSequence) return;
        _remoteSequence++;
        ProcessPacket(data);
    }

    private void HandleHandshake(ArraySegment<byte> data, byte[] buffer, int offset, PacketType protocol)
    {
        if (protocol != PacketType.Handshake)
        {
            Logger.LogInfo<RudpConnection>($"Expected handshake packet, instead got {protocol}. Disconnecting.");
            Disconnect();
            return;
        }

        offset += 1 + 2;
        offset += buffer.ReadUint32BE(offset, out uint sequence);
        offset += buffer.ReadInt32BE(offset, out int sessionId);
        offset += buffer.ReadInt32BE(offset, out int secretNum);
        offset += buffer.ReadUint32BE(offset, out uint gamedataSequence);

        _remoteSequence = sequence + 1;
        SessionId = sessionId;
        _secret = secretNum;
        _remoteGamedataSequence = gamedataSequence;

        Session? session = BombdServer.Instance.SessionManager.Get(this);
        if (session == null)
        {
            Logger.LogInfo<RudpConnection>("Got invalid session id. Disconnecting.");
            Disconnect();
            return;
        }

        HashSalt = session.HashSalt;
        if (!VerifyChecksum16(data, 2))
        {
            Logger.LogInfo<RudpConnection>("Handshake packet checksum failed. Disconnecting.");
            Disconnect();
            return;
        }
        
        RudpMessage msg = _messagePool.Get();
        msg.EncodeHandshake(sessionId, _secret, HashSalt);
        _sendBuffer.Add(msg);

        QueueAck(protocol, sequence);

        Logger.LogDebug<RudpConnection>("Player has connected!");
        
        State = ConnectionState.WaitingForConnection;
    }

    private void HandleAck(PacketType ackingProtocol, uint sequence)
    {
        int index = _sendBuffer.FindIndex(x => x.Sequence == sequence && x.Protocol == ackingProtocol);
        if (index < 0) return;

        RudpMessage acked = _sendBuffer[index];
        uint now = (uint)TimeHelper.LocalTime;

        if (acked.SendCount == 1)
        {
            // Sent once: an unambiguous round trip
            UpdateRoundTripTime(now - acked.FirstSendTime);
            _backoff = 0;
        }
        else if (_hasRttSample && now - acked.Timestamp < _smoothedRtt / 2)
        {
            UpdateRoundTripTime(now - acked.FirstSendTime);
            _spuriousSinceLog++;
            _backoff = 0;
        }
        // Otherwise it was a real loss, recovered by the resend: keep the backoff until a clean measurement arrives

        _sendBuffer.RemoveAt(index);
        _messagePool.Return(acked);
    }
    private void ReceiveReliable(ArraySegment<byte> data, PacketType protocol, uint sequence)
    {
        bool isGamedata = protocol == PacketType.ReliableGameData;
        uint expected = isGamedata ? _remoteGamedataSequence : _remoteSequence;

        if (sequence < expected)
        {
            QueueAck(protocol, sequence);
            return;
        }

        if (sequence > expected)
        {
            if (sequence - expected > MaxReorderAhead) return;

            var early = isGamedata ? _earlyGamedata : _earlyNetcode;
            if (!early.ContainsKey(sequence))
            {
                early[sequence] = data.ToArray();
                _reorderedSinceLog++;
            }
            
            if (EarlyAcks) QueueAck(protocol, sequence);
            return;
        }

        QueueAck(protocol, sequence);
        AdvanceRemoteSequence(isGamedata);
        ProcessPacket(data);
        ReleaseEarlyPackets(protocol);
    }

    private void ReleaseEarlyPackets(PacketType protocol)
    {
        bool isGamedata = protocol == PacketType.ReliableGameData;
        var early = isGamedata ? _earlyGamedata : _earlyNetcode;

        while (State != ConnectionState.Disconnected && early.Count > 0)
        {
            uint expected = isGamedata ? _remoteGamedataSequence : _remoteSequence;
            if (!early.Remove(expected, out byte[]? packet)) break;

            if (!EarlyAcks) QueueAck(protocol, expected);
            AdvanceRemoteSequence(isGamedata);
            ProcessPacket(new ArraySegment<byte>(packet));
        }
    }

    private void AdvanceRemoteSequence(bool isGamedata)
    {
        if (isGamedata) _remoteGamedataSequence++;
        else _remoteSequence++;
    }

    private void QueueAck(PacketType protocol, uint sequence)
    {
        _ackList.Add(new RudpAckRecord { Protocol = protocol, SequenceNumber = sequence });
    }

    private void ProcessPacket(ArraySegment<byte> data)
    {
        if (State == ConnectionState.Disconnected) return;

        int offset = data.Offset;
        byte[] buffer = data.Array!;
        var protocol = (PacketType)buffer[offset++];

        if (protocol == PacketType.Handshake)
        {
            Logger.LogInfo<RudpConnection>(
                "Connection attempted handshake while already connected. Disconnecting.");
            Disconnect();
            return;
        }

        if (IsAuthenticating)
        {
            if (protocol != PacketType.ReliableNetcodeData)
            {
                Logger.LogInfo<RudpConnection>(
                    "Expected netcode data during authentication. Disconnecting.");
                Disconnect();
                return;
            }
            
            offset += buffer.ReadBoolean(offset, out bool groupCompleteFlag);
            offset += buffer.ReadUint16BE(offset, out ushort groupId);
            offset += 4 + 4 + 4;
            
            if (groupId != _remoteGroupNumber)
            {
                Logger.LogInfo<RudpConnection>("Got incorrect group number during authentication. Disconnecting.");
                Disconnect();
                return;
            }
            
            int size = data.Count - (offset - data.Offset);
            
            if (_groupOffset + size > MaxNetcodeSize)
            {
                Logger.LogInfo<RudpConnection>("Got netcode data that's about to exceed group buffer capacity. This shouldn't happen. Disconnecting.");
                Disconnect();
                return;
            }
            
            Buffer.BlockCopy(buffer, offset, _groupBuffer, _groupOffset, size);
            _groupOffset += size;

            if (groupCompleteFlag)
            {
                var netcode = new ArraySegment<byte>(_groupBuffer, 0, _groupOffset);
                if (State == ConnectionState.WaitingForConnection) HandleStartConnect(netcode);
                else HandleTimeSync(netcode);
                _groupOffset = 0;
                _remoteGroupNumber++;
            }

            return;
        }

        // The only types of messages that we need to support now that we're connected
        // are just the gamedata messages, so either reliable or unreliable gamedata.
        if (protocol == PacketType.UnreliableGameData)
        {
            offset += 1 + 2;
            offset += buffer.ReadUint16BE(offset, out ushort payloadBytes);
            offset += 2;
            var unreliableMessage = new ArraySegment<byte>(buffer, offset, payloadBytes);
            CountMessageType(protocol, unreliableMessage);
            Service.OnData(this, unreliableMessage, protocol);
        }
        else if (protocol == PacketType.ReliableGameData)
        {
            offset += 1 + 1;
            byte groupFlags = buffer[offset++];
            offset += 4;
            offset += buffer.ReadUint16BE(offset, out ushort groupId);
            offset += buffer.ReadUint16BE(offset, out ushort groupSizeBytes);
            offset += 2;
            offset += buffer.ReadUint16BE(offset, out ushort payloadBytes);
            
            if (groupId != _remoteGroupNumber)
            {
                Logger.LogInfo<RudpConnection>("Got incorrect group number in reliable gamedata message. Disconnecting.");
                Disconnect();
                return;
            }
            
            if (_groupOffset + payloadBytes > MaxGamedataSize)
            {
                Logger.LogInfo<RudpConnection>("Got gamedata that's about to exceed group buffer capacity. This shouldn't happen. Disconnecting.");
                Disconnect();
                return;
            }
            
            bool isGroupComplete;
            if (Platform == Platform.Karting)
            {
                isGroupComplete = (groupFlags & 0x80) != 0;
                EnsureGroupCapacity(groupSizeBytes | ((groupFlags & 0x7f) << 16));
            }
            else isGroupComplete = (groupFlags != 0);
            
            Buffer.BlockCopy(buffer, offset, _groupBuffer, _groupOffset, payloadBytes);
            _groupOffset += payloadBytes;

            if (isGroupComplete)
            {
                var reliableMessage = new ArraySegment<byte>(_groupBuffer, 0, _groupOffset);
                CountMessageType(protocol, reliableMessage);
                Service.OnData(this, reliableMessage, protocol);
                _groupOffset = 0;
                _remoteGroupNumber++;
            }
        }
        else
            Logger.LogInfo<RudpConnection>($"Unsupported protocol: {protocol}");
    }

    private static readonly Dictionary<(PacketType, NetMessageType), int> s_messageCounts = new();
    private static long s_lastMessageCountLog;

    private void CountMessageType(PacketType protocol, ArraySegment<byte> message)
    {
        using NetworkReaderPooled reader = NetworkReaderPool.Get(message);
        if (!NetworkMessages.Unpack(reader, Platform, out NetMessageType type, out _, out _)) return;

        lock (s_messageCounts)
        {
            s_messageCounts.TryGetValue((protocol, type), out int count);
            s_messageCounts[(protocol, type)] = count + 1;

            long now = Environment.TickCount64;
            if (now - s_lastMessageCountLog < 60_000) return;
            s_lastMessageCountLog = now;

            foreach (var ((channel, messageType), n) in s_messageCounts.OrderByDescending(x => x.Value))
                Logger.LogInfo<RudpConnection>($"Received in the last minute: {messageType} via {channel}: {n}");
            s_messageCounts.Clear();
        }
    }

    private void EnsureGroupCapacity(int value)
    {
        if (_groupBuffer.Length >= value) return;
        int capacity = Math.Max(value, _groupBuffer.Length * 2);
        Array.Resize(ref _groupBuffer, capacity);
    }

    private void UpdateRoundTripTime(uint sample) // Based off of TCP protocol to help with slower connections and being more forgiving
    {
        if (!_hasRttSample)
        {
            _smoothedRtt = sample;
            _rttVariance = sample / 2f;
            _hasRttSample = true;
        }
        else
        {
            _rttVariance = 0.75f * _rttVariance + 0.25f * Math.Abs(_smoothedRtt - sample);
            _smoothedRtt = 0.875f * _smoothedRtt + 0.125f * sample;
        }
        
        float rto = _smoothedRtt + Math.Max(4 * _rttVariance, 200f);
        _resendTimeout = Math.Clamp((int)rto, MinResendTime, MaxResendTime);
    }

    private void FillSendWindow()
    {
        if (_pendingSend.Count == 0) return;

        int netcodeInFlight = 0, gamedataInFlight = 0;
        foreach (RudpMessage m in _sendBuffer)
        {
            if (m.Protocol == PacketType.ReliableNetcodeData) netcodeInFlight++;
            else if (m.Protocol == PacketType.ReliableGameData) gamedataInFlight++;
        }

        for (int i = 0; i < _pendingSend.Count;)
        {
            RudpMessage m = _pendingSend[i];
            bool isNetcode = m.Protocol == PacketType.ReliableNetcodeData;
            if ((isNetcode ? netcodeInFlight : gamedataInFlight) >= MaxInFlight)
            {
                i++;
                continue;
            }

            if (isNetcode) netcodeInFlight++;
            else gamedataInFlight++;
            
            _sendBuffer.Add(m);
            _pendingSend.RemoveAt(i);
        }
    }

    internal void Update()
    {
        uint time = (uint)TimeHelper.LocalTime;

        if (time >= _lastReceiveTime + PacketTimeout)
        {
            Logger.LogDebug<RudpConnection>("Connection timed out. Disconnecting.");
            Disconnect();
            return;
        }

        FillSendWindow();

        int timeout = Math.Min(_resendTimeout << _backoff, MaxResendTime);
        int resends = 0;

        foreach (RudpMessage message in _sendBuffer)
        {
            if (message.SendCount == 0) message.FirstSendTime = time;
            else if (time - message.Timestamp < timeout) continue;
            else resends++;

            message.SendCount++;
            message.Timestamp = time;
            _server.Send(this, message.GetArraySegment());
        }

        if (resends > 0 && _backoff < MaxBackoff) _backoff++;
        _resendsSinceLog += resends;

        if (_ackList.Count > 0)
        {
            RudpMessage ackMessage = _messagePool.Get();
            foreach (RudpAckRecord ack in _ackList)
            {
                ackMessage.EncodeAck(ack.Protocol, ack.SequenceNumber);
                _server.Send(this, ackMessage.GetArraySegment());
                ackMessage.Reset();
            }

            _messagePool.Return(ackMessage);

            _ackList.Clear();
        }

        LogDiagnostics(time);
    }

    private void LogDiagnostics(uint time)
    {
        if (_resendsSinceLog == 0 && _reorderedSinceLog == 0 && _spuriousSinceLog == 0) return;
        if (time - _lastDiagnosticsLog < DiagnosticsInterval) return;

        Logger.LogWarning<RudpConnection>(
            $"{Username} ({Endpoint}): {_resendsSinceLog} resent, {_spuriousSinceLog} unnecessary, " +
            $"{_reorderedSinceLog} received out of order, " +
            $"{_sendBuffer.Count} in flight, {_pendingSend.Count} queued, rtt {_smoothedRtt:0}ms, " +
            $"timeout {Math.Min(_resendTimeout << _backoff, MaxResendTime)}ms");

        _lastDiagnosticsLog = time;
        _resendsSinceLog = 0;
        _reorderedSinceLog = 0;
        _spuriousSinceLog = 0;
    }

    public override void Send(ArraySegment<byte> data, PacketType protocol)
    {
        switch (protocol)
        {
            case PacketType.ReliableGameData:
            {
                SendReliable(PacketType.ReliableGameData, data);
                break;
            }
            case PacketType.ReliableNetcodeData:
            {
                SendReliable(PacketType.ReliableNetcodeData, data);
                break;
            }
            case PacketType.UnreliableGameData:
            {
                RudpMessage msg = _messagePool.Get();
                msg.EncodeUnreliableGamedata(data, HashSalt);
                _server.Send(this, msg.GetArraySegment());
                _messagePool.Return(msg);
                break;
            }
            case PacketType.VoipData:
            {
                RudpMessage msg = _messagePool.Get();
                msg.EncodeVoipData((uint)TimeHelper.LocalTime, data, Platform);
                _server.Send(this, msg.GetArraySegment());
                _messagePool.Return(msg);
                break;
            }
        }
    }

    public override void Disconnect()
    {
        if (State == ConnectionState.Disconnected) return;

        RudpMessage msg = _messagePool.Get();
        msg.EncodeReset(_localSequence++, _secret);
        _server.Send(this, msg.GetArraySegment());
        _messagePool.Return(msg);

        if (State > ConnectionState.WaitingForConnection) Service.OnDisconnected(this);

        State = ConnectionState.Disconnected;

        _server.ConnectionsToRemove.Add(Endpoint);
    }

    private void SendReliable(PacketType protocol, ArraySegment<byte> data)
    {
        int len = data.Count;

        int count;
        if (len <= MaxPayloadSize) count = 1;
        else count = (len + MaxPayloadSize - 1) / MaxPayloadSize;
        
        ushort groupNumber = _localGroupNumber++;
        int groupSize = len;
        
        int offset = 0;
        for (int i = 0; i < count; ++i)
        {
            int size = len > MaxPayloadSize ? MaxPayloadSize : len;
            RudpMessage msg = _messagePool.Get();
            var slice = new ArraySegment<byte>(data.Array!, data.Offset + offset, size);

            offset += size;
            len -= size;

            if (protocol == PacketType.ReliableNetcodeData)
                msg.EncodeNetcode(groupNumber, _localSequence++, slice, len == 0, HashSalt);
            else
                msg.EncodeGamedata(groupNumber, _localGamedataSequence++, groupSize, slice, len == 0, HashSalt);

            _pendingSend.Add(msg);
        }
    }
}