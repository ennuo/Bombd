using System.IO;
using System.Text;
using BombServerEmu_MNR.Src.DataTypes;
using BombServerEmu_MNR.Src.Helpers;
using BombServerEmu_MNR.Src.Protocols.Clients;

namespace BombServerEmu_MNR.Src.Services
{
    class GameServer
    {
        public static uint ServerNameUID = 0x4e14793e;
        public static uint CoiUID = 0x221C7BAF;
        
        public BombService Service { get; }

        public GameServer(string ip, ushort port)
        {
            Service = new BombService("gameserver", EProtocolType.RUDP, true, ip, port);
            Service.RegisterMethod("startConnect", Connect.StartConnectHandler);
            Service.RegisterMethod("timeSyncRequest", Connect.TimeSyncRequestHandler);

            Service.RegisterDirectConnect(DirectConnectHandler, EEndianness.Big);
        }
        
        void DirectConnectHandler(IClient clientInterface, EndiannessAwareBinaryReader br, EndiannessAwareBinaryWriter bw)
        {
            var client = (RUDPClient)clientInterface;
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
            var state = client.State;
            if (state == 1 && !client.HasPendingGamedataAcks())
            {
                // This should probably be moved somewhere else for cleanliness,
                // but I'm tired as hell, so it is what it is.
                var coi = new[]
                {
                    new CircleOfInfluence.Theme
                    {
                        Type =  CircleOfInfluence.Type.Single,
                        Name = "Hotseat",
                        Index = 2,
                        Events =
                        {
                            new CircleOfInfluence.Event
                            {
                                Name = "playin' king like it's poker",
                                Id = 71025,
                                Laps = 3,
                                Description = "What even goes here?"
                            }
                        }
                        
                    },
                    new CircleOfInfluence.Theme
                    {
                        Type =  CircleOfInfluence.Type.Single,
                        Name = "DLC Demo",
                        Index = 3,
                        Events =
                        {
                            new CircleOfInfluence.Event
                            {
                                Description = "Disabled"
                            }
                        }
                    },
                    new CircleOfInfluence.Theme
                    {
                        Type = CircleOfInfluence.Type.Series,
                        Name = "Showcase",
                        Index = 0
                    },
                    new CircleOfInfluence.Theme
                    {
                        Type = CircleOfInfluence.Type.Series,
                        Name = "Special Event",
                        Index = 1,
                    }
                };

                bw.Write((byte) ENetMessageType.SyncObjectCreate);
                bw.Write((byte) 0);
                bw.Write((ushort) 0x1fa8);
                bw.Write(ServerNameUID);
                bw.Write(CoiUID);
                bw.Write(1);
                
                foreach (var theme in coi)
                {
                    bw.Write(Encoding.UTF8.GetBytes(theme.Name.PadRight(0x40, '\0')));
                    bw.Write(Encoding.UTF8.GetBytes(theme.Url.PadRight(0x80, '\0')));
                    bw.Write(theme.Index);

                    // Series support up to 10 events, singles obviously only have a single
                    var len = theme.Type == CircleOfInfluence.Type.Series ? 10 : 1;
                    for (var i = 0; i < len; ++i)
                    {
                        if (i >= theme.Events.Count)
                        {
                            bw.Write(new byte[0x14c]);
                            continue;
                        }
                        
                        var evt = theme.Events[i];
                        bw.Write(Encoding.UTF8.GetBytes(evt.Name.PadRight(0x40, '\0')));
                        bw.Write(evt.Id);
                        bw.Write(1);
                        bw.Write(1);
                        bw.Write(evt.Laps);
                        
                        // Don't feel like figuring out what these fields are right now
                        bw.Write(new byte[]
                        {
                            0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
                            0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0xFF, 0xFF, 0xFF, 0xFF,
                            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x04, 0x32, 0x33, 0x0A, 0x00
                        });
                        bw.Write(new byte[0x44]);
                        bw.Write(Encoding.UTF8.GetBytes(evt.Description.PadRight(0x80, '\0')));
                    }
                    
                }
                
                client.SendReliableGameData(bw);
                client.State = 2;
            }
            
            if (state == 0)
            {
                bw.Write((byte) ENetMessageType.RandomSeed);
                bw.Write((byte) 0);
                bw.Write((ushort) 0x000c);
                bw.Write(ServerNameUID); 
                bw.Write(0xdf81c28a);
                client.SendReliableGameData(bw);
                
                bw.BaseStream.SetLength(0);
                bw.Write((byte) ENetMessageType.SyncObjectCreate);
                bw.Write((byte) 0);
                bw.Write((ushort) 0x0058);
                bw.Write(ServerNameUID);
                bw.Write(CoiUID);
                bw.Write(0);
                bw.Write(Encoding.ASCII.GetBytes("simserver".PadRight(0x20, '\0')));
                bw.Write(Encoding.ASCII.GetBytes("coiInfo".PadRight(0x20, '\0')));
                bw.Write((uint) ENetObjectType.NetCoiInfoPackage);
                bw.Write(CoiUID);
                client.SendReliableGameData(bw);
                
                client.State = 1;
            }
            else if (br.BaseStream.Length != 0)
            {
                var type = (ENetMessageType)br.ReadByte();
                
                if (type == ENetMessageType.PlayerStateUpdate)
                {
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
            
            client.SendPendingGamedataAcks();
        }
    }
}
