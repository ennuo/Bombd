using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using BombServerEmu_MNR.Src.Helpers;
using BombServerEmu_MNR.Src.Helpers.Extensions;

namespace BombServerEmu_MNR.Src.DataTypes
{
    class GameManagerGame
    {
        public string GameName { get; set; } = string.Empty;
        public string GameBrowserName { get; set; } = string.Empty;
        public int GameId { get; set; }
        public List<GameManagerPlayer> Players { get; set; } = new List<GameManagerPlayer>();
        public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();

        public byte[] SerializePlayerList()
        {
            using (var ms = new MemoryStream())
            using (var bw = new EndiannessAwareBinaryWriter(ms, EEndianness.Big))
            {
                foreach (var player in Players)
                {
                    bw.Write(player.ToArray());
                }
                
                bw.Flush();
                return ms.ToArray();
            }
        }

        public byte[] SerializeAttributes()
        {
            using (var ms = new MemoryStream())
            using (var bw = new EndiannessAwareBinaryWriter(ms, EEndianness.Big))
            {
                bw.Write(Attributes.Count);
                foreach (var attribute in Attributes)
                {
                    bw.WriteStringMember(attribute.Key);
                    bw.WriteStringMember(attribute.Value);
                }
                
                bw.Flush();
                return ms.ToArray();
            }
        }
    }
}