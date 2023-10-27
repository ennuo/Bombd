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
    class GameBrowserBusiestGames : List<int>
    {
        public GameBrowserBusiestGames() { }
        public GameBrowserBusiestGames(byte[] data)
        {
            if (data.Length <= 0)
                return;
            using (var ms = new MemoryStream(data))
            using (var br = new EndiannessAwareBinaryReader(ms, EEndianness.Big))
            {
                int size = br.ReadInt32();
                for (int i=0; i<size; i++)
                {
                    int item = br.ReadInt32();
                    if (!Contains(item))
                        Add(item);
                }
            }
        }

        public byte[] SerializeList()
        {
            using (var ms = new MemoryStream())
            using (var bw = new EndiannessAwareBinaryWriter(ms, EEndianness.Big))
            {
                bw.Write(Count);
                foreach (var id in this)
                {
                    bw.Write(id);
                }
                return ms.ToArray();
            }
        }
    }
}
