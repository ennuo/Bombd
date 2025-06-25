using Bombd.Core;
using Bombd.Types.Network;
using Bombd.Types.Network.Simulation;

namespace Bombd.ChatCommands
{
    public class ForceStart : IChatCommand
    {
        public string Name => "ForceStart";

        public void Execute(SimServer server, GamePlayer player, string[] args)
        {
            NetChatMessage message;
            if (server.Type != ServerType.Competitive)
            {
                message = ChatCommandManager.GetChatMessage(player, $"{server.Type} is not a race");
                player.SendMessage(message, NetMessageType.TextChatMsg);
                return;
            }
            message = ChatCommandManager.GetChatMessage(player, $"Race was focefully started by {player.Username}");
            message.Private = 0;
            message.Recipient = "";
            server.Broadcast(message, NetMessageType.TextChatMsg);
            server.ForceStart = true;
        }
    }
}
