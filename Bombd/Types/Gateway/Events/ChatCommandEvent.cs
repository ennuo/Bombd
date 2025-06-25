using System.Text.Json.Serialization;

namespace Bombd.Types.Gateway.Events
{
    public class ChatCommandEvent
    {
        [JsonPropertyName("playerId")] public int PlayerConnectId { get; set; }
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("command")] public string Command { get; set; } = string.Empty;
        [JsonPropertyName("args")] public string[] Arguments { get; set; } = [];
    }
}
