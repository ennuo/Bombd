using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BombServerEmu_MNR.Src.Protocols.Clients;
using BombServerEmu_MNR.Src.DataTypes;
using BombServerEmu_MNR.Src.Helpers.Extensions;
using BombServerEmu_MNR.Src.Helpers;

namespace BombServerEmu_MNR.Src.Services
{
    class GameManager
    {

        public BombService Service { get; }
        public static uint HashSalt = 1396788308;
        

        public GameManager(string ip, ushort port)
        {
            Service = new BombService("gamemanager", EProtocolType.TCP, false, ip, port, "output.pfx", "1234");

            Service.RegisterMethod("startConnect", Connect.StartConnectHandler);
            Service.RegisterMethod("timeSyncRequest", Connect.TimeSyncRequestHandler);

            Service.RegisterMethod("logClientMessage", null);
            Service.RegisterMethod("registerSessionKeyWithTargetBombd", null);
            Service.RegisterMethod("createGame", null);
            Service.RegisterMethod("joinGame", JoinGame);
            Service.RegisterMethod("joinEmptyGame", null);
            Service.RegisterMethod("leaveGame", null);
            Service.RegisterMethod("leaveCurrentGame", LeaveCurrentGameHandler);
            Service.RegisterMethod("reserveGame", null);
            Service.RegisterMethod("reserveGameSlotsForPlayers", null);
            Service.RegisterMethod("dropReservedGame", null);
            Service.RegisterMethod("migrateToGame", null);
            Service.RegisterMethod("requestDirectHostConnection", null);
            Service.RegisterMethod("directConnectionStatus", null);
            Service.RegisterMethod("publishAttributes", null);
            Service.RegisterMethod("kickPlayer", null);
            //LBPK requests those from game manager, but not sure about MNR
            Service.RegisterMethod("hostGame", null); //called when your friend is connecting to your pod
            Service.RegisterMethod("listGamesMatchmaking", null); //seems to have the same response as listGames, but i'll leave it as null for now
            Service.RegisterMethod("beginMatchmaking", null);
            Service.RegisterMethod("cancelMatchmaking", null);
            Service.RegisterMethod("requestPlayerCount", null); //requested for each planet that has levels that need matchmaking, the request contains a binary that has a list of level ids on that planet
            Service.RegisterMethod("RequestGlobalPlayerCount", null); //not sure, got called randomly while i was in the pod menu, probably returns an integer to display how many player are currently in the game
            Service.RegisterMethod("requestBusiestCount", null); //LBPK requests it when you are trying to search for busiest levels in the community tab, from what i understand it should return a list of level ids that the game will use as a filter

            Service.RegisterDirectConnect(DirectConnectHandler, EEndianness.Big);
        }
        
        void JoinGame(BombService service, IClient client, BombXml xml)
        {
            var gameName = xml.GetParam("gamename");
            var gameserver = Program.Services.FirstOrDefault(match => match.Name == "gameserver");
            
            xml.SetMethod("joinGame");
            xml.AddParam("listenIP", gameserver != null ? gameserver.IP : "127.0.0.1");
            xml.AddParam("listenPort",  gameserver != null ? gameserver.Port : 50002); 
            xml.AddParam("hashSalt", GameManager.HashSalt.ToString());
            xml.AddParam("sessionId", "1");
            client.SendNetcodeData(xml);
            
            xml.SetMethod("requestDirectHostConnection");
            xml.SetTransactionType(BombXml.TRANSACTION_TYPE_REQUEST);
            xml.AddParam("listenIP", gameserver != null ? gameserver.IP : "127.0.0.1");
            xml.AddParam("listenPort",  gameserver != null ? gameserver.Port : 50002); 
            xml.AddParam("hashSalt", GameManager.HashSalt.ToString());
            xml.AddParam("sessionId", "1");
            client.SendNetcodeData(xml);
            
            
            
            // TODO: Keep track of actual games and send back
            // proper username/id/etc

            var game = new GameManagerGame()
            {
                GameName = gameName,
                GameBrowserName = gameName,
                GameId = 1
            };
            
            game.Players.Add(new GameManagerPlayer
            {
                PlayerId = 1,
                UserId = client.UserId,
                UserName = client.Username,
                GuestCount = 0
            });
            
            game.Attributes.Add("COMM_CHECKSUM", "186793");
            
            Thread.Sleep(2000);
            xml.SetMethod("joinGameCompleted");
            xml.SetTransactionType(BombXml.TRANSACTION_TYPE_REQUEST);
            xml.AddParam("gamename", game.GameName);
            xml.AddParam("gamebrowsername", game.GameBrowserName);
            xml.AddParam("gameid", game.GameId);
            xml.AddParam("numplayerslist", game.Players.Count);
            xml.AddParam("playerlist", Convert.ToBase64String(game.SerializePlayerList()));
            xml.AddParam("attributes", Convert.ToBase64String(game.SerializeAttributes()));
            
            client.SendNetcodeData(xml);
        }

        void DirectConnectHandler(IClient client, EndiannessAwareBinaryReader br, EndiannessAwareBinaryWriter bw)
        {
            bw.Write(new byte[0xFF]);
            client.SendUnreliableGameData(bw);
        }

        void CreateGameHandler(BombService service, IClient client, BombXml xml)
            {
                //gamename,internalIP,externalIP,listenPort
                //xml.SetMethod("createGame");
                //client.SendNetcodeData(xml);
            }

            void LeaveCurrentGameHandler(BombService service, IClient client, BombXml xml)
            {
                xml.SetMethod("leaveCurrentGame");
                client.SendNetcodeData(xml);
            }
        }
}
