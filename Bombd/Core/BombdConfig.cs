using System.Text.Json;
using Bombd.Logging;

namespace Bombd.Core;

public class BombdConfig
{
    public static readonly BombdConfig Instance;
    static BombdConfig()
    {
        BombdConfig? config;
        string path = Path.Combine(Environment.CurrentDirectory, "bombd.json");
        
        if (File.Exists(path))
        {
            string data = File.ReadAllText(path);
            config = JsonSerializer.Deserialize<BombdConfig>(data);
        }
        else
        {
            config = new BombdConfig();
            path = Path.Combine(Environment.CurrentDirectory, "bombd.example.json");
            if (!File.Exists(path))
            {
                Logger.LogInfo<BombdConfig>($"Bombd config doesn't exist! Example configuration is being created at {path}");
                string data = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, data);   
            }
            else
            {
                Logger.LogInfo<BombdConfig>($"Bombd config doesn't exist! Example configuration is located at {path}");
            }
            
            Environment.Exit(0);
        }

        Instance = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    ///     The IP this server will listen on.
    /// </summary>
    public string ListenIP { get; set; } = "0.0.0.0";

    /// <summary>
    ///     The external IP of this server
    /// </summary>
    public string ExternalIP { get; set; } = "127.0.0.1";

    /// <summary>
    ///     The address of the Web API
    /// </summary>
    public string ApiURL { get; set; } = "http://127.0.0.1:10050";

    /// <summary>
    ///     The path to the certificate to be used for services
    /// </summary>
    public string PfxCertificate { get; set; } = string.Empty;

    /// <summary>
    ///     The key used for the certificate
    /// </summary>
    public string PfxKey { get; set; } = string.Empty;

    /// <summary>
    ///     How many times each service performs an update in a second.
    /// </summary>
    public int TickRate { get; set; } = 15;

    /// <summary>
    ///     The maximum log level to log messages from.
    /// </summary>
    public string MaxLogLevel { get; set; } = Enum.GetName(LogLevel.Info);
    
    /// <summary>
    ///     Whether or not connections from LittleBigPlanet Karting are allowed.
    /// </summary>
    public bool AllowKarting { get; set; } = false;
    
    /// <summary>
    ///     Whether or not connections from ModNation Racers are allowed.
    /// </summary>
    public bool AllowModNation { get; set; } = true;

    /// <summary>
    ///     The key used for server communication api.
    /// </summary>
    public string ServerCommunicationKey { get; set; } = string.Empty;
}