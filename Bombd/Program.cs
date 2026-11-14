using Bombd.Core;
using Bombd.Logging;
using Bombd.Services;
using Directory = Bombd.Services.Directory;

Logger.SetLogMaxLevel(Enum.Parse<LogLevel>(BombdConfig.Instance.MaxLogLevel));

string certificate = BombdConfig.Instance.PfxCertificate;
if (string.IsNullOrEmpty(certificate))
{
    Logger.LogInfo<Program>("A certificate is required in order to start the server!");
    return;
}

if (!File.Exists(certificate))
{
    Logger.LogError<Program>($"{certificate} doesn't exist!");
    return;
}

var server = new BombdServer();

server.AddService<Directory>();
server.AddService<Matchmaking>();
server.AddService<GameManager>();
server.AddService<GameBrowser>();

server.Start();