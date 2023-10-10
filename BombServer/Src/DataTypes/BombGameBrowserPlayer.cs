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
    class BombGameBrowserPlayer
    {
        public string PlayerName { get; set; } = string.Empty;
        public List<string> Guests { get; set; } = new List<string>();
        
        public byte[] ToArray()
        {
            using (var ms = new MemoryStream())
            using (var bw = new EndiannessAwareBinaryWriter(ms, EEndianness.Big))
            {
                bw.WriteStringMember(PlayerName);
                bw.Write(Guests.Count);
                foreach (var guest in Guests)
                {
                    bw.WriteStringMember(guest);
                }
                
                bw.Flush();
                return ms.ToArray();
            }
        }
    }
}