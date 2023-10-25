using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BombServerEmu_MNR.Src.Protocols.Clients;
using BombServerEmu_MNR.Src.DataTypes;
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
            xml.SetMethod("beginMatchmaking");
            client.SendNetcodeData(xml);
            xml.SetMethod("beginMatchmaking");
            xml.SetTransactionType(BombXml.TRANSACTION_TYPE_REQUEST);
            client.SendNetcodeData(xml);
            xml.SetMethod("matchmakingBegin");
            xml.SetTransactionType(BombXml.TRANSACTION_TYPE_REQUEST);
            xml.AddParam("matchmakingBeginTime", (int)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond));
            client.SendNetcodeData(xml);
        }

        void CancelMatchmakingHandler(BombService service, IClient client, BombXml xml)
        {
            xml.SetMethod("cancelMatchmaking");
            client.SendNetcodeData(xml);
            xml.SetMethod("cancelMatchmaking");
            xml.SetTransactionType(BombXml.TRANSACTION_TYPE_REQUEST);
            client.SendNetcodeData(xml);
            xml.SetMethod("matchmakingCancel");
            xml.SetTransactionType(BombXml.TRANSACTION_TYPE_REQUEST);
            client.SendNetcodeData(xml);
        }
    }
}
