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
    class BombGameManagerPlayer
    {
        public int PlayerId { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public int GuestCount { get; set; }

        public byte[] ToArray()
        {
            using (var ms = new MemoryStream())
            using (var bw = new EndiannessAwareBinaryWriter(ms, EEndianness.Big))
            {
                bw.Write(PlayerId);
                bw.Write(UserId);
                bw.Write(Encoding.ASCII.GetBytes(UserName.PadRight(0x20, '\0')));
                bw.Write(GuestCount);
                
                bw.Flush();
                return ms.ToArray();
            }
        }
    }
}