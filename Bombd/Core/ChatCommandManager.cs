using Bombd.ChatCommands;
using Bombd.Types.Network;
using Bombd.Types.Network.Simulation;
using System.Text;

namespace Bombd.Core
{
    public class ChatCommandManager
    {
        private static List<IChatCommand> Commands = [];
        private static bool CommandsRegistered = false;

        private static void RegisterCommands()
        {
            CommandsRegistered = true;

            if (BombdConfig.Instance.EnableDebugCommands) //maybe add some kind of permission system for stuff like that...?
            {
                RegisterChatCommand(new ForceStart());
                RegisterChatCommand(new DisableAI());
            }
        }

        public static void RegisterChatCommand(IChatCommand command)
        {
            if (!Commands.Any(match => match.Name == command.Name))
                Commands.Add(command);
        }

        public static void ProcessChatCommand(SimServer server, GamePlayer player, string commandName, string[] args)
        {
            if (!CommandsRegistered)
                RegisterCommands();

            IChatCommand? command = Commands.FirstOrDefault(match => match.Name.ToLower().Equals(commandName.ToLower()));

            if (command == null)
            {
                player.SendMessage(GetChatMessage(player, $"Unknown command {commandName}"), NetMessageType.TextChatMsg);
                return;
            }

            command.Execute(server, player, args);
        }

        public static NetChatMessage GetChatMessage(GamePlayer player, string message) => new()
        {
            Platform = player.Platform,
            Private = 1,
            Sender = $"System message",
            Body = Convert.ToBase64String(Encoding.ASCII.GetBytes(message)),
            Recipient = player.Username
        };
    }
}
