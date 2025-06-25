using Bombd.Core;
using Bombd.Types.Network.Simulation;

namespace Bombd.ChatCommands
{
    public class ServerCommChatCommand(string name) : IChatCommand
    {
        public string Name => name;

        public void Execute(SimServer server, GamePlayer player, string[] args)
        {
            BombdServer.Comms.SendChatCommand(player.UserId, player.State.PlayerConnectId, Name, args);
        }
    }
}
