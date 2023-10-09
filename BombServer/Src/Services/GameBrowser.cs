using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using BombServerEmu_MNR.Src.Protocols.Clients;
using BombServerEmu_MNR.Src.DataTypes;
using BombServerEmu_MNR.Src.Log;

namespace BombServerEmu_MNR.Src.Services
{
    class GameBrowser
    {
        public BombService Service { get; }

        public GameBrowser(string ip, ushort port)
        {
            Service = new BombService("gamebrowser", EProtocolType.TCP, false, ip, port, "output.pfx", "1234");
            Service.RegisterMethod("startConnect", Connect.StartConnectHandler);
            Service.RegisterMethod("timeSyncRequest", Connect.TimeSyncRequestHandler);

            Service.RegisterMethod("subscribeGameEvents", null);
            Service.RegisterMethod("unSubscribeGameEvents", UnSubscribeGameEventsHandler);
            Service.RegisterMethod("listGames", ListGamesHandler);
            Service.RegisterMethod("listFakeGames", null);
        }

        void ListGamesHandler(BombService service, IClient client, BombXml xml)
        {
            var attributes = new BombAttributeList(Convert.FromBase64String(xml.GetParam("attributes")));
            Logging.Log(typeof(GameBrowser), "{0}", LogType.Debug, attributes);

            //This response is 120% correct, investigate the matchmaking config, thats likely why the game wont create a game
            xml.SetMethod("listGames");

            var timeOfDeath = Math.Floor((DateTime.UtcNow.AddHours(1) - new DateTime(1970, 1, 1)).TotalSeconds);

            var gameList = new BombGameList
            {
                TimeOfDeath = (int) timeOfDeath,
                ClusterUuid = Program.ClusterUuid,
                GameManagerIP = "127.0.0.1",
                GameManagerPort = "10505",
                GameManagerUUID = GameManager.UUID
            };

            var game = new BombGame
            {
                GameName = "debug_kart_lobby",
                DisplayName = "KartPark"
            };
            
            game.GameAttributes.Add("__IS_RANKED", "0");
            game.GameAttributes.Add("__JOIN_MODE", "OPEN");
            game.GameAttributes.Add("__MM_MODE_G", "OPEN");
            game.GameAttributes.Add("__MAX_PLAYERS", "8");
            
            gameList.Add(game);

            xml.AddParam("serverGameListHeader", Convert.ToBase64String(gameList.SerializeHeader()));
            xml.AddParam("serverGameList", Convert.ToBase64String(gameList.SerializeList()));
            xml.AddParam("gameListTimeOfDeath", timeOfDeath);
            client.SendNetcodeData(xml);
        }

        void UnSubscribeGameEventsHandler(BombService service, IClient client, BombXml xml)
        {
            xml.SetMethod("unSubscribeGameEvents");
            //TODO: Unsubscribe from game events
            client.SendNetcodeData(xml);
        }
    }
}
