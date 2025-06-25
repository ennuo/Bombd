using Bombd.Core;
using Bombd.Types.Network.Simulation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bombd.ChatCommands
{
    public class DisableAI : IChatCommand
    {
        public string Name => "noai";

        public void Execute(SimServer server, GamePlayer player, string[] args)
        {
            server.DisableAI();
        }
    }
}
