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
using BombServerEmu_MNR.Src.Log;

namespace BombServerEmu_MNR.Src.Services
{
    //Im not so sure the game uses P2P, as a just in case
    class GameServer
    {
        public BombService Service { get; }

        public GameServer(string ip, ushort port)
        {
            Service = new BombService("gameserver", EProtocolType.RUDP, true, ip, port);
            Service.RegisterMethod("startConnect", Connect.StartConnectHandler);
            Service.RegisterMethod("timeSyncRequest", Connect.TimeSyncRequestHandler);

            Service.RegisterDirectConnect(DirectConnectHandler, EEndianness.Big);
        }
        
        void DirectConnectHandler(IClient client, EndiannessAwareBinaryReader br, EndiannessAwareBinaryWriter bw)
        {
            // Gamedata messages
            //  u8 Type
            //  u8 SizeExtra, dummy data? Or does it matter?
            //  u16 Size
            //  u32 SenderNameID
            
            // Messages are probably different between MNR and Karting, so let's see...
            
            // When a connection is made, a bunch of data is sent, there's a lot of unreliable
            // data sent that I'm not sure the identity of, so we'll focus on reliable since
            // these are the packets that I assume the client/server must receive.
            
                // Server - Reliable - eNET_MESSAGE_TYPE_RANDOM_SEED
                    // The server sends a seed to the player, should be unique per "session?"
                    // Payload is a single integer seed
                    
            
                
                // Server - Reliable - eNET_MESSAGE_PLAYER_SESSION_INFO x PlayerCount
                
                // Server - Reliable - eNET_MESSAGE_SYNC_OBJECT_CREATE
                    // OwnerName = simserver
                    // DebugTag = coiInfo
                    // Type = 5 (SeriesInfo?)
                    // GUID = 0x221c7baf (Should this be random or is it supposed to be specific?)
                
                // Client - Reliable - eNET_MESSAGE_PLAYER_STATE_UPDATE
                
            // Give the client enough data to launch into modspot
            var state = ((RUDPClient)client).State;
            if (state == 0)
            {
                bw.Write((byte) ENetMessageType.RandomSeed);
                bw.Write((byte) 0);
                bw.Write((ushort) 0x000c);
                bw.Write(0x4e14793e); 
                bw.Write(0xdf81c28a);
                
                client.SendReliableGameData(bw);
                
                ((RUDPClient)client).State = 1;
            }
        }
    }
}
