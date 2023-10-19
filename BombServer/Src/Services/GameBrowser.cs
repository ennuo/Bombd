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

            Service.RegisterMethod("subscribeGameEvents", SubscribeGameEventsHandler);
            Service.RegisterMethod("unSubscribeGameEvents", UnSubscribeGameEventsHandler);
            Service.RegisterMethod("listGames", ListGamesHandler);
            Service.RegisterMethod("listFakeGames", null);
        }

        public static void FillDummyGameData(BombService service, IClient client, BombXml xml)
        {
            var timeOfDeath = Math.Floor((DateTime.UtcNow.AddHours(1) - new DateTime(1970, 1, 1)).TotalSeconds);
            var gamemanager = Program.Services.FirstOrDefault(match => match.Name == "gamemanager");
            
 
            xml.AddParam("gameListTimeOfDeath", timeOfDeath);
            
            var gameList = new ServerGameList
            {
                TimeOfDeath = (int) timeOfDeath,
                ClusterUuid = Program.ClusterUuid,
                GameManagerIP = gamemanager != null ? gamemanager.IP : "127.0.0.1",
                GameManagerPort = gamemanager != null ? gamemanager.Port.ToString() : "10505",
                GameManagerUUID = gamemanager != null ? gamemanager.Uuid : ""
            };

            var game = new GameBrowserGame
            {
                GameName = "debug_kart_lobby",
                DisplayName = "KartPark"
            };
            
            game.Attributes.Add("__IS_RANKED", "0");
            game.Attributes.Add("__JOIN_MODE", "OPEN");
            game.Attributes.Add("__MM_MODE_G", "OPEN");
            game.Attributes.Add("__MAX_PLAYERS", "8");
            
            gameList.Add(game);

            xml.AddParam("serverGameListHeader", Convert.ToBase64String(gameList.SerializeHeader()));
            xml.AddParam("serverGameList", Convert.ToBase64String(gameList.SerializeList()));
        }

        void ListGamesHandler(BombService service, IClient client, BombXml xml)
        {
            var attributes = new GameBrowserAttributes(Convert.FromBase64String(xml.GetParam("attributes")));
            Logging.Log(typeof(GameBrowser), "{0}", LogType.Debug, attributes);
            
            xml.SetMethod("listGames");
            FillDummyGameData(service, client, xml);
            client.SendNetcodeData(xml);
        }

        void SubscribeGameEventsHandler(BombService service, IClient client, BombXml xml)
        {
            xml.SetMethod("subscribeGameEvents");
            FillDummyGameData(service, client, xml);
            client.SendNetcodeData(xml);
        }

        void UnSubscribeGameEventsHandler(BombService service, IClient client, BombXml xml)
        {
            xml.SetMethod("unSubscribeGameEvents");
            client.SendNetcodeData(xml);
        }
    }
}
