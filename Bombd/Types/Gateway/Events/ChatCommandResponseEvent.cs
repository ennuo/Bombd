using System.Text.Json.Serialization;

namespace Bombd.Types.Gateway.Events
{
    public class ChatCommandResponseEvent
    {
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("broadcastInRoom")] public bool BroadcastInRoom { get; set; }
        [JsonPropertyName("sender")] public string Sender { get; set; } = string.Empty;
        [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
    }
}
