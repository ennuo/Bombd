using Bombd.Core;
using Bombd.Types.Network.Simulation;

namespace Bombd.ChatCommands
{
    public interface IChatCommand
    {
        public string Name { get; }

        public void Execute(SimServer server, GamePlayer player, string[] args);
    }
}
