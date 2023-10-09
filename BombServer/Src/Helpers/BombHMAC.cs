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
        private static byte[] GetHMAC(byte[] data, uint salt)
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
            
            return MD5.Create().ComputeHash(inner);
        }
        
        public static ushort GetMD516(byte[] data, uint salt)
        {
            var hmac = GetHMAC(data, salt);
            ushort result = 0;
            for (var i = 0; i < 16; i += 2) //Squish down our HMAC into 16 bits
                result ^= (ushort)((hmac[i + 1] << 8) ^ hmac[i]);
            return result;
        }
        
        public static uint GetMD532(byte[] data, uint salt)
        {
            var hmac = GetHMAC(data, salt);
            uint result = 0;
            for (var i = 0; i < 16; i += 4) //Squish down our HMAC into 32 bits
                result ^= (uint)(hmac[i] ^ (hmac[i + 1] << 8) ^ (hmac[i + 2] << 16) ^ (hmac[i + 3] << 24));
            return result;
        }
    }
}
