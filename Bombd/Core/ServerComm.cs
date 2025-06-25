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
using Bombd.ChatCommands;
using NetMessageType = Bombd.Types.Network.NetMessageType;

namespace Bombd.Core;

public sealed class ServerComm : IDisposable
{
    private const int MaxMessageSize = 4096;
    private const string MasterServer = "API";

    private ClientWebSocket _socket;
    private readonly Uri _uri;
    
    private readonly Channel<GatewayMessage> _pendingMessages = Channel.CreateUnbounded<GatewayMessage>();
    private readonly Aes _aes = Aes.Create();
    private readonly bool _hasKey;
    
    public ServerComm()
    {
        string url = BombdConfig.Instance.ApiURL.TrimEnd('/');
        url = url.StartsWith("http") ? url.Replace("http", "ws") : url.Replace("https", "wss");
        _uri = new Uri($"{url}/api/Gateway");
        
        string key = BombdConfig.Instance.ServerCommunicationKey;
        _hasKey = !string.IsNullOrEmpty(key);
        
        _aes.Mode = CipherMode.ECB;
        _aes.Padding = PaddingMode.Zeros;
        if (_hasKey)
        {
            _aes.Key = Encoding.UTF8.GetBytes(key);
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

    public void SendChatCommand(int userId, int playerId, string command, string[] args)
    {
        DispatchEvent(GatewayEvents.PlayerUpdated, new ChatCommandEvent
        {
            PlayerConnectId = playerId,
            UserId = userId,
            Command = command,
            Arguments = args
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
        byte[] buffer = new byte[MaxMessageSize];
        while (_socket.State == WebSocketState.Open)
        {
            await foreach (GatewayMessage message in _pendingMessages.Reader.ReadAllAsync())
            {
                string json = JsonSerializer.Serialize(message);
                if (!string.IsNullOrEmpty(BombdConfig.Instance.ServerCommunicationKey))
                    json = Encrypt(json);

                Logger.LogDebug<ServerComm>("SEND: " + message.Type);
                Logger.LogDebug<ServerComm>("DATA: " + message.Content);
                
                int len = Encoding.UTF8.GetBytes(json, 0, json.Length, buffer, 0);
                var payload = new ArraySegment<byte>(buffer, 0, len);
                if (_socket.State == WebSocketState.Open)
                    await _socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);   
            }
        }
    }
    
    private async Task Receive()
    {
        byte[] buffer = new byte[MaxMessageSize];
        while (_socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _socket.ReceiveAsync(buffer, CancellationToken.None);
            }
            catch (Exception)
            {
                Logger.LogError<ServerComm>($"There was an error receiving message");
                break;
            }
            
            if (result.MessageType == WebSocketMessageType.Text)
            {
                string payload = Encoding.UTF8.GetString(buffer, 0, result.Count);
                try
                {
                    if (_hasKey) payload = Decrypt(payload);
                    var message = JsonSerializer.Deserialize<GatewayMessage>(payload);
                    if (message != null)
                        OnMessage(message);
                }
                catch (Exception e)
                {
                    Logger.LogDebug<ServerComm>($"Failed to process message: {e}");
                }
            }
            else if (result.MessageType == WebSocketMessageType.Close) break;
        }

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
            case GatewayEvents.RegisterChatCommands:
            {
                //Suggestion: maybe allow other servers to register chat commands? can't think of a potential use case though...
                if (ParseMessage(message, out RegisterChatCommandsEvent? evt) && message.From == MasterServer)
                    foreach (var command in evt.Commands)
                        ChatCommandManager.RegisterChatCommand(new ServerCommChatCommand(command));
                break;
            }
            case GatewayEvents.ChatCommandResponse:
            {
                if (ParseMessage(message, out ChatCommandResponseEvent? evt))
                {
                    var room = BombdServer.Instance.RoomManager.GetRoomByUser(evt.UserId);

                    if (room != null)
                    {
                        var player = room.GetUser(evt.UserId);

                        var chatMessage = ChatCommandManager.GetChatMessage(player, evt.Message);
                        if (!string.IsNullOrEmpty(evt.Sender))
                            chatMessage.Sender = evt.Sender;

                        if (evt.BroadcastInRoom)
                        {
                            chatMessage.Private = 0;
                            chatMessage.Recipient = "";
                            room.Simulation.Broadcast(chatMessage, NetMessageType.TextChatMsg);
                        }
                        else
                            player.SendMessage(chatMessage, NetMessageType.TextChatMsg);
                    }
                }
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

    private string Encrypt(string message)
    {
        using var stream = new MemoryStream();
        using var cryptoTransform = _aes.CreateEncryptor(_aes.Key, null);
        using var cryptoStream = new CryptoStream(stream, cryptoTransform, CryptoStreamMode.Write);
        cryptoStream.Write(Encoding.UTF8.GetBytes(message));
        
        return Convert.ToBase64String(stream.ToArray());
    }

    private string Decrypt(string message)
    {
        using var stream = new MemoryStream(Convert.FromBase64String(message));
        using var cryptoTransform = _aes.CreateDecryptor(_aes.Key, null);
        using var cryptoStream = new CryptoStream(stream, cryptoTransform, CryptoStreamMode.Read);
        using var streamReader = new StreamReader(cryptoStream, Encoding.UTF8);
        
        return streamReader.ReadToEnd();
    }

    public void Dispose()
    {
        _socket.Dispose();
        _aes.Dispose();
    }
}