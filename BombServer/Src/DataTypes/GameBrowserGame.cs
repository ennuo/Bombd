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
    class GameBrowserGame
    {
        public List<GameBrowserPlayer> Players { get; set; } = new List<GameBrowserPlayer>();
        public int TimeSinceLastPlayerJoin { get; set; }
        public string GameName { get; set; }
        public string DisplayName { get; set; }
        public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();

        public byte[] ToArray(bool karting = false)
        {
            using (var ms = new MemoryStream())
            using (var bw = new EndiannessAwareBinaryWriter(ms, EEndianness.Big))
            {
                bw.Write(Players.Count);
                bw.Write(TimeSinceLastPlayerJoin);

                foreach (var player in Players)
                {
                    bw.Write(player.ToArray());
                }
                
                bw.WriteStringMember(GameName);
                bw.WriteStringMember(DisplayName);

                bw.Write(Attributes.Count);
                foreach (var attribute in Attributes)
                {
                    bw.WriteStringMember(attribute.Key);
                    bw.WriteStringMember(attribute.Value);
                }

                //NumFreeSlots
                if (karting)
                    bw.Write(64);

                bw.Flush();
                return ms.ToArray();
            }
        }
    }
}
