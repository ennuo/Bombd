using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace BombServerEmu_MNR.Src.Helpers
{
    class BombHMAC
    {
        public static ushort GetHMACMD5(byte[] data, int salt)
        {
            var outer = new byte[4 + data.Length];
            var inner = new byte[20];

            for (var i = 0; i < 4; ++i)
            {
                var b = (byte)(salt >> ((3 - i) * 8));
                outer[i] = (byte)(b ^ 0x5c);
                inner[i] = (byte)(b ^ 0x36);
            }

            Buffer.BlockCopy(data, 0, outer, 4, data.Length);
            var hash = MD5.Create().ComputeHash(outer);
            Buffer.BlockCopy(hash, 0, inner, 4, hash.Length);
            hash = MD5.Create().ComputeHash(inner);
            
            ushort result = 0;
            for (int i=0; i<16; i+=2) //Squish down our HMAC into 16 bits
                result ^= (ushort)((hash[i + 1] << 8) ^ hash[i]);
            return result;
        }
    }
}
