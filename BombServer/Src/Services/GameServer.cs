﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using BombServerEmu_MNR.Src.Protocols.Clients;
using BombServerEmu_MNR.Src.DataTypes;
using BombServerEmu_MNR.Src.Helpers;
using BombServerEmu_MNR.Src.Helpers.Extensions;
using BombServerEmu_MNR.Src.Log;

namespace BombServerEmu_MNR.Src.Services
{
    //Im not so sure the game uses P2P, as a just in case
    class GameServer
    {
        public BombService Service { get; }

        public GameServer(ushort port)
        {
            Service = new BombService("gameserver", EProtocolType.RUDP, true, "127.0.0.1", port);
            Service.RegisterMethod("startConnect", Connect.StartConnectHandler);
            Service.RegisterMethod("timeSyncRequest", Connect.TimeSyncRequestHandler);

            Service.RegisterDirectConnect(DirectConnectHandler, EEndianness.Big);
        }

        void DirectConnectHandler(IClient client, EndiannessAwareBinaryReader br, EndiannessAwareBinaryWriter bw)
        {
            var state = ((RUDPClient)client).State;
            if (state == 0)
            {
                bw.Write(new byte[0x78]);
                client.SendUnreliableGameData(bw);

                ((RUDPClient)client).State = 1;
            }
        }
    }
}
