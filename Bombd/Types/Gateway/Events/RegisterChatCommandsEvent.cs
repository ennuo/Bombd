using System.Text.Json.Serialization;

namespace Bombd.Types.Gateway.Events
{
    public class RegisterChatCommandsEvent
    {
        [JsonPropertyName("commands")] public string[] Commands { get; set; } = [];
    }
}
