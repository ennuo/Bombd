using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        public static string UUID;
        public static uint HashSalt = 1396788308;
        

        public GameManager(string ip, ushort port)
        {
            Service = new BombService("gamemanager", EProtocolType.TCP, false, ip, port, "output.pfx", "1234");
            UUID = Service.Uuid;

            Service.RegisterMethod("startConnect", Connect.StartConnectHandler);
            Service.RegisterMethod("timeSyncRequest", Connect.TimeSyncRequestHandlerDEBUG);

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
            Service.RegisterMethod("requestDirectHostConnection", RequestDirectHostConnection);    //???
            Service.RegisterMethod("directConnectionStatus", null);
            Service.RegisterMethod("publishAttributes", null);
            Service.RegisterMethod("kickPlayer", null);

            Service.RegisterDirectConnect(DirectConnectHandler, EEndianness.Big);
        }

        void RequestDirectHostConnection(BombService service, IClient client, BombXml xml)
        {
            xml.AddParam("hashSalt", GameManager.HashSalt.ToString());
            xml.AddParam("sessionId", "1");
            xml.AddParam("listenIP", "127.0.0.1");
            xml.AddParam("listenPort", 50002); 
            
            client.SendNetcodeData(xml);
        }


        void JoinGame(BombService service, IClient client, BombXml xml)
        {
            xml.SetMethod("joinGame");
            xml.AddParam("hashSalt", GameManager.HashSalt.ToString());
            xml.AddParam("sessionId", "1");
            xml.AddParam("listenIP", "127.0.0.1");
            xml.AddParam("listenPort", 50002); 
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
