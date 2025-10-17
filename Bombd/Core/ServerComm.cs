using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Bombd.Logging;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Bombd.Types.Gateway;
using Bombd.Types.Gateway.Events;
using Directory = Bombd.Services.Directory;

namespace Bombd.Core;

public sealed class ServerComm : IDisposable
{
    private const int MaxMessageSize = 16384;
    private readonly int EncryptedHeaderSize = AesGcm.NonceByteSizes.MaxSize + AesGcm.TagByteSizes.MaxSize;
    private const string MasterServer = "API";

    private ClientWebSocket _socket;
    private readonly Uri _uri;
    
    private readonly Channel<GatewayMessage> _pendingMessages = Channel.CreateUnbounded<GatewayMessage>();
    private readonly AesGcm? _aes;

    private readonly byte[] _decryptBuffer = new byte[MaxMessageSize];
    private readonly byte[] _encryptBuffer = new byte[MaxMessageSize];
    private readonly byte[] _sendBuffer = new byte[MaxMessageSize];
    private readonly byte[] _recvBuffer = new byte[MaxMessageSize];
    private CancellationTokenSource _source = new();
    
    public ServerComm()
    {
        string url = BombdConfig.Instance.ApiURL.TrimEnd('/');
        url = url.StartsWith("http") ? url.Replace("http", "ws") : url.Replace("https", "wss");
        _uri = new Uri($"{url}/api/Gateway");
        
        string key = BombdConfig.Instance.ServerCommunicationKey;
        if (!string.IsNullOrEmpty(key))
        {
            _aes = new AesGcm(Encoding.UTF8.GetBytes(key), AesGcm.TagByteSizes.MaxSize);    
        }
    }

    public void NotifyEventStarted(int trackId, List<int> playerIds)
    {
        DispatchEvent(GatewayEvents.EventStarted, new EventStartedEvent
        {
            PlayerIds = playerIds,
            TrackId = trackId
        });
    }

    public void NotifyEventFinished(int trackId, List<PlayerEventStats> stats, bool IsMNR, string GameType, bool IsRanked)
    {
        DispatchEvent(GatewayEvents.EventFinished, new EventFinishedEvent
        {
            Stats = stats,
            TrackId = trackId,
            IsMNR = IsMNR,
            GameType = GameType,
            IsRanked = IsRanked
        });
    }

    public void NotifyPlayerQuit(int pcId, bool disconnect)
    {
        DispatchEvent(GatewayEvents.PlayerQuit, new PlayerQuitEvent
        {
            Disconnected = disconnect,
            PlayerConnectId = pcId
        });
    }
    
    public void UpdatePlayerCount(int playerCount)
    {
        DispatchEvent(GatewayEvents.UpdatePlayerCount, new UpdatePlayerCountEvent
        {
            PlayerCount = playerCount
        });
    }
    
    public void UpdatePlayerData(int pcId, int charId, int kartId)
    {
        DispatchEvent(GatewayEvents.PlayerUpdated, new PlayerUpdatedEvent
        {
            PlayerConnectId = pcId,
            CharacterId = charId,
            KartId = kartId
        });
    }
    
    public async Task Run()
    {
        while (true)
        {
            BombdServer.Instance.SessionManager.Clear();
            
            while (!await Connect())
            {
                Logger.LogWarning<ServerComm>("Could not connect to Web API waiting before retrying...");
                await Task.Delay(5000); // Wait some amount of time before retrying connection
            }

            if (_source.IsCancellationRequested)
            {
                _source.Dispose();
                _source = new CancellationTokenSource();
            }
            
            Logger.LogInfo<ServerComm>("Successfully connected to Web API!");
            
            WebApiManager.Initialize();
            Task send = Task.Run(async () => await Send());
            Task receive = Task.Run(async () => await Receive());
            
            DispatchEvent(GatewayEvents.ServerInfo, new ServerInfoEvent
            {
                Type = "DIRECTORY",
                Address = BombdConfig.Instance.ExternalIP,
                Port = BombdServer.Instance.GetService<Directory>().Port,
                ServerPrivateKey = "MIGrAgEAAiEAq0cOe8L1tOpnc7e+ouVD" //is this even used for bombd? 
            });
            
            await Task.WhenAll(send, receive);
            _socket.Dispose();
            
            Logger.LogWarning<ServerComm>("Connection to the Web API was lost!");
        }
    }

    private async Task<bool> Connect()
    {
        var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("server_id", BombdServer.Instance.ClusterUuid);
        try
        {
            await ws.ConnectAsync(_uri, CancellationToken.None);
        }
        catch (Exception)
        {
            ws.Dispose();
            return false;
        }
        
        _socket = ws;
        
        return true;
    }
    
    private async Task Send()
    {
        try
        {
            while (await _pendingMessages.Reader.WaitToReadAsync(_source.Token))
            {
                while (_pendingMessages.Reader.TryRead(out GatewayMessage? message))
                {
                    Logger.LogDebug<ServerComm>("SEND: " + message.Type);
                    Logger.LogDebug<ServerComm>("DATA: " + message.Content);

                    string json = JsonSerializer.Serialize(message);
                    int len = EncryptIntoSendBuffer(json);
                    var type = _aes != null ? WebSocketMessageType.Binary : WebSocketMessageType.Text;
                    var payload = new ArraySegment<byte>(_sendBuffer, 0, len);

                    if (_socket.State == WebSocketState.Open)
                        await _socket.SendAsync(payload, type, true, _source.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore!
        }
        
        Logger.LogDebug<ServerComm>("Send thread is closing down...");
    }
    
    private async Task Receive()
    {
        while (_socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _socket.ReceiveAsync(_recvBuffer, _source.Token);
            }
            catch (OperationCanceledException)
            {
                // Nothing to handle...
                break;
            }
            catch (Exception)
            {
                Logger.LogError<ServerComm>($"There was an error receiving message");
                break;
            }

            string payload;
            if (result.MessageType == WebSocketMessageType.Text)
            {
                if (_aes != null)
                {
                    Logger.LogWarning<ServerComm>("Received unencrypted message, ignoring!");
                    continue;
                }

                payload = Encoding.UTF8.GetString(_recvBuffer, 0, result.Count);
            }
            else if (result.MessageType == WebSocketMessageType.Binary)
            {
                if (_aes == null)
                {
                    Logger.LogWarning<ServerComm>("Received encrypted message, ignoring!");
                    continue;
                }

                if (result.Count < EncryptedHeaderSize)
                {
                    Logger.LogWarning<ServerComm>($"Received invalid message!");
                    continue;
                }
                
                try
                {
                    payload = DecryptFromReceiveBuffer(result.Count);
                }
                catch (Exception e)
                {
                    Logger.LogDebug<ServerComm>($"Failed to decrypt message: {e}");
                    continue;
                }
            }
            else break;
            
            try
            {
                var message = JsonSerializer.Deserialize<GatewayMessage>(payload);
                if (message != null)
                    OnMessage(message);
            }
            catch (Exception e)
            {
                Logger.LogDebug<ServerComm>($"Failed to process message: {e}");
            }
        }
        
        Logger.LogDebug<ServerComm>("Read thread is closing down...");
        await _source.CancelAsync();
        
        if (_socket is { State: WebSocketState.Aborted or WebSocketState.Closed or WebSocketState.CloseSent })
            return;
        
        try
        {
            await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
        }
        catch (Exception)
        {
            Logger.LogDebug<ServerComm>($"There was an error while closing connection");
        }
    }

    private static void OnMessage(GatewayMessage message)
    {
        Logger.LogDebug<ServerComm>($"Received {message.Type} from {message.From} with content:");
        Logger.LogDebug<ServerComm>(message.Content);

        switch (message.Type)
        {
            case GatewayEvents.PlayerSessionCreated:
            {
                if (ParseMessage(message, out PlayerSessionCreatedEvent? evt))
                    BombdServer.Instance.SessionManager.Register(evt);
                break;
            }
            case GatewayEvents.PlayerSessionDestroyed:
            {
                if (ParseMessage(message, out PlayerSessionDestroyedEvent? evt))
                    BombdServer.Instance.SessionManager.Unregister(evt);
                break;
            }
            case GatewayEvents.HotSeatPlaylistReset:
            {
                BombdServer.Instance.GameServer.NotifyHotSeatReset();
                break;
            }
        }
    }
    
    private static bool ParseMessage<T>(GatewayMessage message, [MaybeNullWhen(false), NotNullWhen(true)] out T evt)
    {
        evt = JsonSerializer.Deserialize<T>(message.Content);
        if (evt != null) return true;
        Logger.LogError<ServerComm>($"An error occurred while parsing {message.Type}");
        return false;
    }
    
    private void DispatchEvent(string type, object evt)
    {
        var message = new GatewayMessage
        {
            Type = type,
            From = BombdServer.Instance.ClusterUuid,
            To = MasterServer,
            Content = JsonSerializer.Serialize(evt)
        };

        _pendingMessages.Writer.TryWrite(message);
    }

    private int EncryptIntoSendBuffer(string message)
    {
        if (_aes == null)
            return Encoding.UTF8.GetBytes(message, 0, message.Length, _sendBuffer, 0);
        
        int len = Encoding.UTF8.GetBytes(message, 0, message.Length, _encryptBuffer, 0);
        Span<byte> input = _encryptBuffer.AsSpan(0, len);
        
        Span<byte> nonce = _sendBuffer.AsSpan(0, AesGcm.NonceByteSizes.MaxSize);
        Span<byte> tag = _sendBuffer.AsSpan(AesGcm.NonceByteSizes.MaxSize, AesGcm.TagByteSizes.MaxSize);
        Span<byte> output = _sendBuffer.AsSpan(EncryptedHeaderSize, len);
        
        RandomNumberGenerator.Fill(nonce);
        _aes.Encrypt(nonce, input, output, tag);
        return EncryptedHeaderSize + len;
    }

    private string DecryptFromReceiveBuffer(int len)
    {
        Span<byte> nonce = _recvBuffer.AsSpan(0, AesGcm.NonceByteSizes.MaxSize);
        Span<byte> tag = _recvBuffer.AsSpan(AesGcm.NonceByteSizes.MaxSize, AesGcm.TagByteSizes.MaxSize);
        Span<byte> data = _recvBuffer.AsSpan(EncryptedHeaderSize, len - EncryptedHeaderSize);
        Span<byte> output = _decryptBuffer.AsSpan(0, data.Length);
        
        _aes!.Decrypt(nonce, data, tag, output);
        return Encoding.UTF8.GetString(output);
    }

    public void Dispose()
    {
        _socket.Dispose();
        _aes?.Dispose();
    }
}