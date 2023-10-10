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
    class GameServer
    {
        public static uint ServerNameUID = 0x4e14793e;
        
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
            // !!! Everything here is temporary and just meant for testing/reverse engineering
            // !!! the protocols used in multiplayer.
            
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
                bw.Write(ServerNameUID); 
                bw.Write(0xdf81c28a);
                client.SendReliableGameData(bw);

                for (int i = 0; i < 2; ++i)
                {
                    bw.BaseStream.SetLength(0);
                    bw.Write((byte) ENetMessageType.PlayerSessionInfo);
                    bw.Write((byte) 0);
                    bw.Write((ushort) 0x18);
                    bw.Write(ServerNameUID);
                    bw.Write(1);
                    bw.Write(0xae967d6d);
                    bw.Write(0xb0523902);
                    bw.Write(0);
                    client.SendReliableGameData(bw);   
                }
                
                bw.BaseStream.SetLength(0);
                bw.Write((byte) ENetMessageType.PlayerSessionInfo);
                bw.Write((byte) 0);
                bw.Write((ushort) 0x18);
                bw.Write(ServerNameUID);
                bw.Write(1);
                bw.Write(0x9e7e0b96);
                bw.Write(0xb0523902);
                bw.Write(0);
                client.SendReliableGameData(bw);
                
                bw.BaseStream.SetLength(0);
                bw.Write((byte) ENetMessageType.PlayerSessionInfo);
                bw.Write((byte) 0);
                bw.Write((ushort) 0x18);
                bw.Write(ServerNameUID);
                bw.Write(1);
                bw.Write(0xfe3d279c);
                bw.Write(0xb0523902);
                bw.Write(0);
                client.SendReliableGameData(bw);
                
                bw.BaseStream.SetLength(0);
                bw.Write((byte) ENetMessageType.SyncObjectCreate);
                bw.Write((byte) 0);
                bw.Write((ushort) 0x0058);
                bw.Write(ServerNameUID);
                bw.Write(0x221c7baf);
                bw.Write(0);
                bw.Write(Encoding.ASCII.GetBytes("simserver".PadRight(0x20, '\0')));
                bw.Write(Encoding.ASCII.GetBytes("coiInfo".PadRight(0x20, '\0')));
                bw.Write((uint) ENetObjectType.SeriesInfo);
                bw.Write(0x221c7baf);
                client.SendReliableGameData(bw);
                
                ((RUDPClient)client).State = 1;
            }
            else if (br.BaseStream.Length != 0)
            {
                var type = (ENetMessageType)br.ReadByte();
                
                if (type == ENetMessageType.PlayerStateUpdate)
                {
                    // If it's the first update request, create the player
                    // object on the server
                    // if (state == 1)
                    // {
                    //     bw.Write((byte) ENetMessageType.SyncObjectCreate);
                    //     bw.Write((byte) 0);
                    //     bw.Write((ushort) 0x0058);
                    //     bw.Write(ServerNameUID);
                    //     bw.Write(0);
                    //     bw.Write(client.Username.PadRight(0x20, '0'));
                    //     bw.Write(client.Username.PadRight(0x20, '0'));
                    //     bw.Write((uint) ENetObjectType.PlayerConfig);
                    //     bw.Write(client.UserId);
                    //     client.SendReliableGameData(bw);
                    //     
                    //     bw.BaseStream.SetLength(0);
                    //     ((RUDPClient)client).State = 2;
                    // }
                    
                    // Send back a dummy bulk player state update
                    bw.Write((byte) ENetMessageType.BulkPlayerStateUpdate);
                    bw.Write((byte) 0);
                    bw.Write((ushort) 0x24);
                    bw.Write(ServerNameUID);
                    bw.Write(1);
                    bw.Write(837026840); // NameUID
                    bw.Write(2059179); // PcId
                    bw.Write(100);  // KartId
                    bw.Write(1205); // CharacterId
                    bw.Write(0); // Away
                    bw.Write(0); // Mic
                    client.SendReliableGameData(bw);
                    
                }
                

            }
            
            ((RUDPClient)client).SendPendingGamedataAcks();
        }
    }
}
