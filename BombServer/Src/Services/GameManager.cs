using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BombServerEmu_MNR.Src.Log;
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

            Service.RegisterMethod("logClientMessage", LogClientMessageHandler);
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
            Service.RegisterMethod("listGamesMatchmaking", ListGamesMatchmakingHandler); //seems to have the same response as listGames, but i'll leave it as null for now
            Service.RegisterMethod("requestPlayerCount", RequestPlayerCountHandler); //requested for each planet that has levels that need matchmaking, the request contains a binary that has a list of level ids on that planet
            Service.RegisterMethod("RequestGlobalPlayerCount", RequestGlobalPlayerCountHandler); //not sure, got called randomly while i was in the pod menu, probably returns an integer to display how many player are currently in the game
            Service.RegisterMethod("requestBusiestCount", RequestBusiestCountHandler); //LBPK requests it when you are trying to search for busiest levels in the community tab, from what i understand it should return a list of level ids that the game will use as a filter
            Service.RegisterMethod("reserveSlotsInGameForGroup", ReserveSlotsInGameForGroupHandler);

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
            
            // TODO: Keep track of actual games and send back
            // proper username/id/etc

            var game = Program.GamesMatchmaking.FirstOrDefault(match => match.GameName == xml.GetParam("gamename"));
            if (game == null)
            {
                game = new GameManagerGame()
                {
                    GameName = gameName,
                    GameBrowserName = gameName,
                    GameId = 1
                };
            }
            
            game.Players.Add(new GameManagerPlayer
            {
                PlayerId = 1,
                UserId = client.UserId,
                UserName = client.Username,
                GuestCount = 0
            });
            
            if (!game.Attributes.ContainsKey("COMM_CHECKSUM"))
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

        void ListGamesMatchmakingHandler(BombService service, IClient client, BombXml xml)
        {
            var searchData = new GameBrowserSearchData(Convert.FromBase64String(xml.GetParam("searchData")));
            //Logging.Log(typeof(GameBrowser), "{0}", LogType.Debug, attributes);
            
            xml.SetMethod("listGamesMatchmaking");

            var timeOfDeath = (int)(DateTime.UtcNow.AddHours(1).Ticks / TimeSpan.TicksPerMillisecond);
            xml.AddParam("gameListTimeOfDeath", timeOfDeath);

            var gameList = new ServerGameList
            {
                TimeOfDeath = (int) timeOfDeath,
                ClusterUuid = Program.ClusterUuid,
                GameManagerIP = service.IP,
                GameManagerPort = service.Port.ToString(),
                GameManagerUUID = service.Uuid
            };

            if (searchData.PreferredCreationIdList.Count == 0)
            {
                foreach(var Game in Program.GamesMatchmaking)
                {
                    var game = new GameBrowserGame
                    {
                        GameName = Game.GameName,
                        DisplayName = Game.GameBrowserName
                    };

                    foreach(var player in Game.Players)
                    {
                        game.Players.Add(new GameBrowserPlayer
                        {
                            PlayerName = player.UserName
                        });
                    }

                    game.Attributes = Game.Attributes;
                    gameList.Add(game);
                }
            }
            else 
            {
                foreach(var id in searchData.PreferredCreationIdList)
                {
                    foreach(var Game in Program.GamesMatchmaking.Where(match => match.Attributes.ContainsKey("TRACK_CREATIONID") && match.Attributes["TRACK_CREATIONID"] == id.ToString()))
                    {
                        var game = new GameBrowserGame
                        {
                            GameName = Game.GameName,
                            DisplayName = Game.GameBrowserName
                        };

                        foreach(var player in Game.Players)
                        {
                            game.Players.Add(new GameBrowserPlayer
                            {
                                PlayerName = player.UserName
                            });
                        }

                        game.Attributes = Game.Attributes;
                        gameList.Add(game);
                    }
                }
            }
            Logging.Log(typeof(GameBrowser), $"searchData.PreferredCreationIdList.Count = {searchData.PreferredCreationIdList.Count}", LogType.Info);

            Logging.Log(typeof(GameBrowser), $"gameCount = {gameList.Count}", LogType.Info);

            Logging.Log(typeof(GameBrowser), "Filtering attributes", LogType.Info);
            foreach (var Attribute in searchData.Attributes) 
            {
                Logging.Log(typeof(GameBrowser), $"key: {Attribute.Key}\nvalue: {Attribute.Value}", LogType.Info);
                gameList.RemoveAll(match => !match.Attributes.ContainsKey(Attribute.Key) || match.Attributes[Attribute.Key] != Attribute.Value);
            }

            Logging.Log(typeof(GameBrowser), $"gameCount = {gameList.Count}", LogType.Info);

            Logging.Log(typeof(GameBrowser), $"MatchmakingGameCount = {Program.GamesMatchmaking.Count}", LogType.Info);

            xml.AddParam("serverGameListHeader", Convert.ToBase64String(gameList.SerializeHeader()));
            xml.AddParam("serverGameList", Convert.ToBase64String(gameList.SerializeList(true)));

            client.SendNetcodeData(xml);
        }

        void ReserveSlotsInGameForGroupHandler(BombService service, IClient client, BombXml xml)
        {
            //var attributes = new GameBrowserAttributes(Convert.FromBase64String(xml.GetParam("attributes")));
            //Logging.Log(typeof(GameBrowser), "{0}", LogType.Debug, attributes);
            
            xml.SetMethod("reserveSlotsInGameForGroup");
            xml.AddParam("reservationKey", "1");
            client.SendNetcodeData(xml);
        }

        void RequestPlayerCountHandler(BombService service, IClient client, BombXml xml)
        {
            var playerCounters = new GameBrowserPlayerCounters(Convert.FromBase64String(xml.GetParam("requestParams")));
            List<int> keys = new List<int>(playerCounters.Keys);
            foreach (var key in keys) 
            {
                playerCounters[key] = Program.GamesMatchmaking.Where(match => match.Attributes.ContainsKey("TRACK_CREATIONID") 
                    && match.Attributes["TRACK_CREATIONID"] == key.ToString()).Sum(game => game.Players.Count);
                Logging.Log(typeof(GameBrowser), $"gameCount = " + Program.GamesMatchmaking.Where(match => match.Attributes.ContainsKey("TRACK_CREATIONID") 
                    && match.Attributes["TRACK_CREATIONID"] == key.ToString()).Count(), LogType.Info);
                Logging.Log(typeof(GameBrowser), $"playerCount = " + Program.GamesMatchmaking.Where(match => match.Attributes.ContainsKey("TRACK_CREATIONID") 
                    && match.Attributes["TRACK_CREATIONID"] == key.ToString()).Sum(game => game.Players.Count), LogType.Info);
            }
            xml.SetName("gamebrowser");
            xml.SetMethod("requestPlayerCount");
            xml.AddParam("requestParams", Convert.ToBase64String(playerCounters.ToArray()));
            client.SendNetcodeData(xml);
        }

        void RequestGlobalPlayerCountHandler(BombService service, IClient client, BombXml xml)
        {
            xml.SetMethod("RequestGlobalPlayerCount");
            xml.AddParam("GlobalPlayerCount", Program.GamesMatchmaking.Sum(game => game.Players.Count));
            client.SendNetcodeData(xml);
        }

        void RequestBusiestCountHandler(BombService service, IClient client, BombXml xml)
        {
            xml.SetMethod("requestBusiestCount");
            var BusiestGames = new GameBrowserBusiestGames();
            var playerCounts = new Dictionary<int, int>();

            foreach (var game in Program.GamesMatchmaking.Where(match => match.Attributes.ContainsKey("TRACK_CREATIONID"))) 
            {
                if(!playerCounts.ContainsKey(int.Parse(game.Attributes["TRACK_CREATIONID"])))
                    playerCounts.Add(int.Parse(game.Attributes["TRACK_CREATIONID"]), 0);
            }

            foreach (var key in new List<int>(playerCounts.Keys)) 
            {
                playerCounts[key] = Program.GamesMatchmaking.Where(match => match.Attributes.ContainsKey("TRACK_CREATIONID") 
                    && match.Attributes["TRACK_CREATIONID"] == key.ToString()).Sum(game => game.Players.Count);
            }

            var temp = playerCounts.ToList();

            temp.Sort((curr,prev) => prev.Value.CompareTo(curr.Value));

            playerCounts = temp.ToDictionary(x => x.Key, x => x.Value);

            BusiestGames.AddRange(playerCounts.Keys.Take(100));

            xml.AddParam("BusiestGames", Convert.ToBase64String(BusiestGames.SerializeList()));
            client.SendNetcodeData(xml);
        }

        void LogClientMessageHandler(BombService service, IClient client, BombXml xml)
        {
            xml.SetMethod("logClientMessage");
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
            Logging.Log(typeof(GameBrowser), $"MatchmakingGameCount = {Program.GamesMatchmaking.Count}", LogType.Info);
            var game = Program.GamesMatchmaking.FirstOrDefault(match => match.GameName == xml.GetParam("gamename"));
            
            if (game != null)
            {
                game.Players.RemoveAll(match => match.UserName == client.Username);

                if (game.Players.Count == 0)
                    Program.GamesMatchmaking.Remove(game);
            }

            Logging.Log(typeof(GameBrowser), $"MatchmakingGameCount = {Program.GamesMatchmaking.Count}", LogType.Info);

            xml.SetMethod("leaveCurrentGame");
            client.SendNetcodeData(xml);
        }
    }
}
