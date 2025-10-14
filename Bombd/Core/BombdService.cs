using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Bombd.Attributes;
using Bombd.Extensions;
using Bombd.Helpers;
using Bombd.Logging;
using Bombd.Protocols;
using Bombd.Protocols.RUDP;
using Bombd.Protocols.TCP;
using Bombd.Serialization;
using Bombd.Services;
using Bombd.Types.Authentication;
using Bombd.Types.Services;
using JetBrains.Annotations;
using NPTicket;
using NPTicket.Types;
using NPTicket.Verification;
using NPTicket.Verification.Keys;

namespace Bombd.Core;

public abstract class BombdService
{
    private readonly Dictionary<string, MethodInfo> _methods = new();

    private readonly IServer _server;
    private readonly Type _type;
    protected readonly ConcurrentDictionary<int, ConnectionBase> UserInfo = new();
    
    protected BombdService()
    {
        Bombd = BombdServer.Instance;
        _type = GetType();

        ServiceAttribute? serviceAttribute = _type.GetCustomAttributes<ServiceAttribute>().FirstOrDefault();
        if (serviceAttribute == null) throw new Exception("BombdServices require Service attribute!");

        Name = serviceAttribute.Name;
        Port = serviceAttribute.Port;
        Protocol = serviceAttribute.Protocol;

        string protocolName = Protocol.ToString().ToLower();

        Logger.LogInfo(_type, $"Initializing service {Name}:{protocolName}:{Port}");
        Logger.LogInfo(_type, $"-> UID is {Uuid}");

        foreach (MethodInfo method in _type.GetMethods())
        {
            var attributes = method.GetCustomAttributes<TransactionAttribute>();
            foreach (var attribute in attributes)
            {
                _methods[attribute.Method] = method;
                Logger.LogInfo(_type, $"-> Registered '{attribute.Method}' to {_type.Name}::{method.Name}");   
            }
        }
        
        string address = BombdConfig.Instance.ListenIP;
        if (Protocol == ProtocolType.TCP)
        {
            var cert = new X509Certificate(
                BombdConfig.Instance.PfxCertificate,
                BombdConfig.Instance.PfxKey
            );
            _server = new SslServer(address, Port, cert, this);
        }
        else
            _server = new RudpServer(address, Port, this);
        
        Logger.LogInfo(_type, $"Finished initializing service {Name}:{protocolName}:{Port}");
    }

    protected BombdServer Bombd { get; }

    public string Uuid { get; } = CryptoHelper.GetRandomUUID();
    public string Name { get; }
    public int Port { get; }
    public ProtocolType Protocol { get; }

    public void Start()
    {
        _server.Start();
        Task.Run(async () =>
        {
            long step = 1000 / BombdConfig.Instance.TickRate;
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(step));
            while (_server.IsActive)
            {
                while (await timer.WaitForNextTickAsync())
                {
                    _server.Tick();
                }
            }
        });
    }

    public void Stop()
    {
        if (!_server.IsActive) return;

        Logger.LogInfo(_type, "Shutting down...");
        _server.Stop();
    }

    public virtual void OnTick()
    {
    }

    protected virtual void OnConnected(ConnectionBase connection)
    {
        Logger.LogInfo(_type, $"{connection.Username} has been connected.");
    }

    public bool Login(ConnectionBase connection, NetcodeTransaction request, NetcodeTransaction response)
    {
        Ticket ticket;
        byte[] ticketData;
        try
        {
            string encodedPsnTicket = request["NPTicket"]; 
            ticketData = Convert.FromBase64String(encodedPsnTicket);
            ticket = Ticket.ReadFromBytes(ticketData);
        }
        catch (Exception)
        {
            response.Error = "ticketParseFail";
            return false;
        }
		
		bool isRPCN = ticket.SignatureIdentifier == "RPCN" || ticket.IssuerId == 0x33333333;
		bool isPSN = !isRPCN && ticket.IssuerId == 0x100;
		
		TicketVerifier verifier;
		if (isRPCN)
		{
			verifier = new TicketVerifier(ticketData, ticket, RpcnSigningKey.Instance);
		}
		else if (isPSN)
		{
			verifier = new TicketVerifier(ticketData, ticket, UfgSigningKey.Instance);
		}
		else
		{
			response.Error = "invalidTickerIssuerId";
			return false;
		}

        if (!verifier.IsTicketValid())
        {
            response.Error = "invalidTicket";
            return false;
        }
        
        Platform platform = PlatformHelper.FromTitleId(ticket.ServiceId[7..^3]);
        switch (platform)
        {
            case Platform.Unknown:
                response.Error = "unknownGameServiceId";
                return false;
            case Platform.Karting when !BombdConfig.Instance.AllowKarting:
            case Platform.ModNation when !BombdConfig.Instance.AllowModNation:
                return false;
        }
		
        int userId = CryptoHelper.StringHash32Upper(ticket.Username + (isRPCN ? "RPCN" : "PSN"));
        if (UserInfo.ContainsKey(userId))
        {
            Logger.LogError(_type, $"User already has a session with state {UserInfo[userId].State}");
            response.Error = "alreadyLoggedIn";
            return false;
        }
        
        if (request.TryGet("SessionUUID", out string? sessionUuid))
            connection.SessionId = SessionManager.GetSessionKey(sessionUuid);
        else if (request.TryGet("SessionKey", out string? encodedSessionKey))
        {
            // The login requests add the SessionKey param twice, so just take the first one.
            encodedSessionKey = encodedSessionKey.Split(",")[0];
            
            byte[] sessionKeyBytes;
            try
            {
                sessionKeyBytes = Convert.FromBase64String(encodedSessionKey);
            }
            catch (Exception)
            {
                response.Error = "invalidSessionKey";
                return false;
            }

            if (sessionKeyBytes.Length != 4)
            {
                response.Error = "invalidSessionKey";
                return false;
            }

            sessionKeyBytes.ReadInt32BE(0, out int sessionKey);
            connection.SessionId = sessionKey;
        }

        connection.Username = ticket.Username;
        connection.UserId = userId;
        connection.Platform = platform;
        
        // Make sure the user logging in has a session on PlayerConnect.
        Session? session = Bombd.SessionManager.Get(connection);
        if (session == null)
        {
            response.Error = "notLoggedIn";
            return false;
        }
        
        // Check if the ticket matches the session on the PlayerConnect server
        if (session.Username != ticket.Username || session.Issuer != ticket.IssuerId)
        {
            response.Error = "ticketMismatch";
            return false;
        }
        
        // Used so other players can communicate with this player
        // without having access to user-specific session data.
        UserInfo[connection.UserId] = connection;

        if (this is GameServer)
        {
            // I don't think the game even cares what value it gets here,
            // so long as it can find the gameserver parameter.
            response["gameserver"] = "directConnection";
            return true;
        }

        response["bombd_version"] = "3.2.8";
        response["bombd_builddate"] = "3/29/2010 4:52:54 PM";
        response["bombd_OS"] = "1";
        response["bombd_ServerIP"] = BombdConfig.Instance.ExternalIP;
        response["bombd_ServerPort"] = Port.ToString();
        response["serveruuid"] = Uuid;
        response["clusteruuid"] = Bombd.ClusterUuid;
        response["username"] = ticket.Username;
        response["userid"] = connection.UserId.ToString();

        // Only the GameManager needs to be sent the matchmaking configuration.
        if (this is GameManager)
        {
            byte[] config = File.ReadAllBytes($"Data/Matchmaking/{platform}.xml");
            response["MMConfigFile"] = Convert.ToBase64String(config);
            response["MMConfigFileSize"] = config.Length.ToString();
        }
        
        // Connection in the context of a BombdService is after authentication,
        // we don't care about any connections being connected until they've
        // actually verified their identity
        OnConnected(connection);
        
        return true;
    }

    private object? HandleServiceTransaction(ConnectionBase connection, NetcodeTransaction request,
        NetcodeTransaction response, BombdService? redirect = null)
    {
        // This is a kind of weird solution, but it's only the case in LittleBigPlanet Karting
        // that all gamebrowser requests get redirected to the gamemanager.
        BombdService service = this;
        if (redirect != null)
        {
            Logger.LogTrace(_type, $"HandleServiceTransaction: Transaction is being redirected to {redirect.Name}");
            service = redirect;
        }

        if (service._methods.TryGetValue(request.MethodName, out MethodInfo? method))
        {
            try
            {
                Session? session = Bombd.SessionManager.Get(connection);
                if (session == null)
                {
                    response.Error = "notLoggedIn";
                    return null;
                }
                
                var context = new TransactionContext
                {
                    Session = session,
                    Connection = connection,
                    Request = request,
                    Response = response
                };

                return method.Invoke(service, [context]);
            }
            catch (Exception e)
            {
                Logger.LogError(_type,
                    $"HandleServiceTransaction: An error occurred while processing transaction {request.MethodName}");
                Logger.LogError(_type, e.ToString());
                response.Error = "internalServerError";
            }
        }
        else
        {
            Logger.LogInfo(_type, $"HandleServiceTransaction: Got unregistered method {request.MethodName}");
            response.Error = "methodNotFound";
        }

        return null;
    }

    private void OnNetcodeData(ConnectionBase connection, ArraySegment<byte> data)
    {
        string xml = Encoding.UTF8.GetString(data);

        NetcodeTransaction request;
        try
        {
            request = new NetcodeTransaction(xml);
        }
        catch (Exception)
        {
            // We can't send an error response back to the client if we don't even know what transaction we got
            // in the first place, so it's best to just drop the connection.
            Logger.LogWarning(_type, "OnNetcodeData: Failed to parse netcode data. Dropping connection.");
            connection.Disconnect();
            return;
        }

        if (request.MethodName != "logClientMessage")
            Logger.LogInfo(_type, $"HandleServiceTransaction: Got transaction ({request.MethodName})");

        NetcodeTransaction response = request.MakeResponse();
        object? value = null;

        // Karting has weird behaviors
        bool isGameBrowserRedirect = request.ServiceName == "gamebrowser" && Name == "gamemanager";
        bool isTemporaryGameManager = request.ServiceName == "temp_gamemanager" && Name == "gamemanager";
        
        if (request.ServiceName == Name || isGameBrowserRedirect || isTemporaryGameManager)
        {
            BombdService? redirect = isGameBrowserRedirect ? Bombd.GetService<GameBrowser>() : null;
            value = HandleServiceTransaction(connection, request, response, redirect);
        }
        else
        {
            Logger.LogInfo(_type, $"OnNetcodeData: Got invalid transaction service {request.ServiceName}");
            response.Error = "serviceMismatch";
        }

        if (value != null && string.IsNullOrEmpty(response.Error)) response.SetObject(value);

        connection.Send(response.ToArraySegment(), PacketType.ReliableNetcodeData);
    }

    public void SendTransaction(int userId, NetcodeTransaction transaction)
    {
        if (UserInfo.TryGetValue(userId, out ConnectionBase? connection))
            connection.Send(transaction.ToArraySegment(), PacketType.ReliableNetcodeData);
    }

    public void SendTransaction(int userId, string method, object value)
    {
        if (!UserInfo.TryGetValue(userId, out ConnectionBase? connection)) return;

        var transaction = NetcodeTransaction.MakeRequest(Name, method, value);
        connection.Send(transaction.ToArraySegment(), PacketType.ReliableNetcodeData);
    }

    public void SendMessage(int userId, ArraySegment<byte> data, PacketType type)
    {
        if (UserInfo.TryGetValue(userId, out ConnectionBase? connection)) connection.Send(data, type);
    }
    
    public void Disconnect(int userId)
    {
        if (UserInfo.TryGetValue(userId, out ConnectionBase? connection)) connection.Disconnect();
    }

    public void OnData(ConnectionBase connection, ArraySegment<byte> data, PacketType type)
    {
        switch (type)
        {
            case PacketType.ReliableNetcodeData:
            {
                OnNetcodeData(connection, data);
                break;
            }
            case PacketType.ReliableGameData:
            case PacketType.UnreliableGameData:
            {
                OnGamedata(connection, data);
                break;
            }
            case PacketType.Ack: break;
            default:
            {
                Logger.LogWarning(_type, $"OnData: Received unsupported packet type ({type})");
                break;
            }
        }
    }

    protected virtual void OnGamedata(ConnectionBase connection, ArraySegment<byte> data)
    {
        Logger.LogWarning(_type, "OnGamedata: This service is unable to handle this packet type.");
    }

    public virtual void OnDisconnected(ConnectionBase connection)
    {
        UserInfo.TryRemove(connection.UserId, out _);
        Logger.LogInfo(_type, $"{connection.Username} has been disconnected.");
    }
}