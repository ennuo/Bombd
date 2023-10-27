using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using BombServerEmu_MNR.Src.Protocols.Clients;
using BombServerEmu_MNR.Src.DataTypes;
using BombServerEmu_MNR.Src.Helpers;
using BombServerEmu_MNR.Src.Helpers.Extensions;

namespace BombServerEmu_MNR.Src.Services
{
    class Matchmaking
    {
        public BombService Service { get; }

        public Matchmaking(string ip, ushort port)
        {
            Service = new BombService("matchmaking", EProtocolType.TCP, false, ip, port, "output.pfx", "1234");
            Service.RegisterMethod("startConnect", Connect.StartConnectHandler);
            Service.RegisterMethod("timeSyncRequest", Connect.TimeSyncRequestHandler);

            Service.RegisterMethod("beginMatchmaking", BeginMatchmakingHandler);
            Service.RegisterMethod("cancelMatchmaking", CancelMatchmakingHandler);
        }

        void BeginMatchmakingHandler(BombService service, IClient client, BombXml xml)
        {
            xml.SetMethod("matchmakingBegin");
            xml.SetTransactionType(BombXml.TRANSACTION_TYPE_REQUEST);
            xml.AddParam("matchmakingBeginTime", (int)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond));
            client.SendNetcodeData(xml);

            var gamemanager = Program.Services.FirstOrDefault(match => match.Name == "gamemanager");

            var game = new GameManagerGame
            {
                GameName = $"{client.Username}-{DateTime.UtcNow.Ticks}",
                GameBrowserName = client.Username,
                GameId = Program.GameIdIncrement
            };

            using (var ms = new MemoryStream(Convert.FromBase64String(xml.GetParam("simpleFilters"))))
            using (var br = new EndiannessAwareBinaryReader(ms, EEndianness.Big))
            {
                int AttributesSize = int.Parse(xml.GetParam("numSimpleFilters"));
                for (int i=0; i<AttributesSize; i++)
                {
                    string key = Encoding.ASCII.GetString(br.ReadBytes(0x20)).Trim('\0');
                    string value = Encoding.ASCII.GetString(br.ReadBytes(0x20)).Trim('\0');
                    if (!game.Attributes.ContainsKey(key))
                        game.Attributes.Add(key, value);
                }
            }

            using (var ms = new MemoryStream(Convert.FromBase64String(xml.GetParam("advancedFilters"))))
            using (var br = new EndiannessAwareBinaryReader(ms, EEndianness.Big))
            {
                int AttributesSize = int.Parse(xml.GetParam("numAdvancedFilters"));
                for (int i=0; i<AttributesSize; i++)
                {
                    string key = Encoding.ASCII.GetString(br.ReadBytes(0x20)).Trim('\0');
                    string value = Encoding.ASCII.GetString(br.ReadBytes(0x20)).Trim('\0');
                    if (!game.Attributes.ContainsKey(key))
                        game.Attributes.Add(key, value);
                }
            }

            Program.GamesMatchmaking.Add(game);
            Program.GameIdIncrement++;

            xml.SetMethod("requestJoinGame");
            //xml.SetMethod("matchmakingError");
            xml.SetTransactionType(BombXml.TRANSACTION_TYPE_REQUEST);
            xml.AddParam("gameName", game.GameName);
            xml.AddParam("host_uuid", gamemanager != null ? gamemanager.Uuid : "");
            xml.AddParam("host_ip", gamemanager != null ? gamemanager.IP : "127.0.0.1");
            xml.AddParam("host_port", gamemanager != null ? gamemanager.Port.ToString() : "10505");
            xml.AddParam("host_cluster_uuid", Program.ClusterUuid);
            client.SendNetcodeData(xml);
        }

        void CancelMatchmakingHandler(BombService service, IClient client, BombXml xml)
        {
            xml.SetMethod("matchmakingCanceled");
            xml.SetTransactionType(BombXml.TRANSACTION_TYPE_REQUEST);
            client.SendNetcodeData(xml);
        }
    }
}
