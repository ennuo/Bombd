using System.Text;
using Bombd.Core;
using Bombd.Helpers;
using Bombd.Logging;
using Bombd.Types.Services;

namespace Bombd.Protocols;

public abstract class ConnectionBase
{
    public readonly IServer Server;

    public readonly BombdService Service;
    public ConnectionState State = ConnectionState.Disconnected;

    protected ConnectionBase(BombdService service, IServer server)
    {
        Service = service;
        Server = server;
    }

    public bool IsAuthenticated => State == ConnectionState.Connected;

    public bool IsAuthenticating =>
        State is ConnectionState.WaitingForConnection or ConnectionState.WaitingForTimeSync;

    public string Username { get; set; } = string.Empty;
    public int UserId { get; set; }
    public Platform Platform { get; set; } = Platform.Unknown;
    public int SessionId { get; set; }
    public uint HashSalt { get; set; } = CryptoHelper.Salt;

    private NetcodeTransaction? ParseConnectTransaction(ArraySegment<byte> data, string method)
    {
        string xml = Encoding.UTF8.GetString(data);
        NetcodeTransaction request;
        try
        {
            request = new NetcodeTransaction(xml);
        }
        catch (Exception)
        {
            Logger.LogWarning<ConnectionBase>(
                "ParseConnectTransaction: Failed to parse netcode data. Dropping connection.");
            Disconnect();
            return null;
        }
        
        // Karting is annoying
        if (request.MethodName == "logClientMessage")
        {
            Send(request.MakeResponse().ToArraySegment(), PacketType.ReliableNetcodeData);
            return null;
        }
        
        if (request.ServiceName != "connect" || request.MethodName != method)
        {
            Logger.LogWarning<ConnectionBase>(
                $"ParseConnectTransaction: Expected connect:{method}, got {request.ServiceName}:{request.MethodName}. Dropping connection.");
            Disconnect();
            return null;
        }

        return request;
    }

    protected void HandleStartConnect(ArraySegment<byte> data)
    {
        NetcodeTransaction? request = ParseConnectTransaction(data, "startConnect");
        if (request == null) return;
        NetcodeTransaction response = request.MakeResponse();

        bool success = Service.Login(this, request, response);

        Send(response.ToArraySegment(), PacketType.ReliableNetcodeData);
        if (!success) Disconnect();
        else State = ConnectionState.WaitingForTimeSync;
    }

    protected void HandleTimeSync(ArraySegment<byte> data)
    {
        NetcodeTransaction? request = ParseConnectTransaction(data, "timeSyncRequest");
        if (request == null) return;
        NetcodeTransaction response = request.MakeResponse();
        response["serverTime"] = TimeHelper.LocalTime.ToString();
        Send(response.ToArraySegment(), PacketType.ReliableNetcodeData);
        State = ConnectionState.Connected;
    }

    public abstract void Send(ArraySegment<byte> data, PacketType protocol);
    public abstract void Disconnect();
}