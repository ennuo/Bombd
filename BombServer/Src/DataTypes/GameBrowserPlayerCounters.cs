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
    class GameBrowserPlayerCounters : Dictionary<int, int>
    {
        string unk1 = "TRACK_CREATIONID";

        public GameBrowserPlayerCounters() { }
        public GameBrowserPlayerCounters(byte[] data)
        {
            if (data.Length <= 0)
                return;
            using (var ms = new MemoryStream(data))
            using (var br = new EndiannessAwareBinaryReader(ms, EEndianness.Big))
            {
                int unk1Length = br.ReadInt32();
                unk1 = Encoding.ASCII.GetString(br.ReadBytes(unk1Length)).Trim('\0');
                int size = br.ReadInt32();
                for (int i=0; i<size; i++)
                {
                    int key = br.ReadInt32();
                    int value = br.ReadInt32();
                    if (!ContainsKey(key))
                        Add(key, value);
                }
            }
        }

        public byte[] ToArray()
        {
            using (var ms = new MemoryStream())
            using (var bw = new EndiannessAwareBinaryWriter(ms, EEndianness.Big))
            {
                bw.WriteStringMember(unk1);
                bw.Write(Keys.Count);
                foreach (var playerCounter in this)
                {
                    bw.Write(playerCounter.Key);
                    bw.Write(playerCounter.Value);
                }
                File.WriteAllBytes("playerCounts.bin", ms.ToArray());
                return ms.ToArray();
            }
        }
    }
}
