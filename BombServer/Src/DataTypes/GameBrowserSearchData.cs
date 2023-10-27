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
    class GameBrowserSearchData
    {
        public int MatchmakingConfigFileVersion;
        public Dictionary<string, string> Attributes = new Dictionary<string, string>();
        public List<string> PreferredPlayerList = new List<string>();
        public int FreeSlotsRequired;
        public List<int> PreferredCreationIdList = new List<int>();

        public GameBrowserSearchData() { }
        public GameBrowserSearchData(byte[] data)
        {
            if (data.Length <= 0)
                return;
            using (var ms = new MemoryStream(data))
            using (var br = new EndiannessAwareBinaryReader(ms, EEndianness.Big))
            {
                MatchmakingConfigFileVersion = br.ReadInt32();

                int AttributesSize = br.ReadInt32();
                for (int i=0; i<AttributesSize; i++)
                {
                    int keyLength = br.ReadInt32();
                    string key = Encoding.ASCII.GetString(br.ReadBytes(keyLength)).Trim('\0');
                    int valueLength = br.ReadInt32();
                    string value = Encoding.ASCII.GetString(br.ReadBytes(valueLength)).Trim('\0');
                    if (!Attributes.ContainsKey(key))
                        Attributes.Add(key, value);
                }

                int PreferredPlayerListSize = br.ReadInt32();
                for (int i=0; i<PreferredPlayerListSize; i++)
                {
                    int itemLength = br.ReadInt32();
                    string item = Encoding.ASCII.GetString(br.ReadBytes(itemLength)).Trim('\0');
                    PreferredPlayerList.Add(item);
                }

                FreeSlotsRequired = br.ReadInt32();

                int PreferredCreationIdListSize = br.ReadInt32();
                for (int i=0; i<PreferredCreationIdListSize; i++)
                {
                    int item = br.ReadInt32();
                    PreferredCreationIdList.Add(item);
                }
            }
        }

        public byte[] ToArray()
        {
            using (var ms = new MemoryStream())
            using (var bw = new EndiannessAwareBinaryWriter(ms, EEndianness.Big))
            {
                bw.Write(MatchmakingConfigFileVersion);

                bw.Write(Attributes.Keys.Count);
                foreach (var Attribute in Attributes)
                {
                    bw.WriteStringMember(Attribute.Key);
                    bw.WriteStringMember(Attribute.Value);
                }

                bw.Write(PreferredPlayerList.Count);
                foreach (var PreferredPlayer in PreferredPlayerList)
                {
                    bw.WriteStringMember(PreferredPlayer);
                }

                bw.Write(FreeSlotsRequired);

                bw.Write(PreferredCreationIdList.Count);
                foreach (var PreferredCreationId in PreferredCreationIdList)
                {
                    bw.Write(PreferredCreationId);
                }

                return ms.ToArray();
            }
        }
    }
}
