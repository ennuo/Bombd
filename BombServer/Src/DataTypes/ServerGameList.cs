using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using BombServerEmu_MNR.Src.Helpers;
using BombServerEmu_MNR.Src.Helpers.Extensions;
using BombServerEmu_MNR.Src.Services;

namespace BombServerEmu_MNR.Src.DataTypes
{

    class ServerGameList : List<GameBrowserGame>
    {
        public int TimeOfDeath { get; set; }
        public string ClusterUuid { get; set; }
        public string GameManagerIP { get; set; }
        public string GameManagerPort { get; set; }
        public string GameManagerUUID { get; set; }

        public byte[] SerializeHeader()
        {
            using (var ms = new MemoryStream())
            using (var bw = new EndiannessAwareBinaryWriter(ms, EEndianness.Big))
            {
                bw.Write(Count);
                bw.Write(TimeOfDeath);

                bw.WriteStringMember(ClusterUuid);
                bw.WriteStringMember(GameManagerIP);
                bw.WriteStringMember(GameManagerPort);
                bw.WriteStringMember(GameManagerUUID);

                bw.Flush();
                return ms.ToArray();
            }
        }

        public byte[] SerializeList(bool karting = false)
        {
            using (var ms = new MemoryStream())
            using (var bw = new EndiannessAwareBinaryWriter(ms, EEndianness.Big))
            {
                foreach (var game in this)
                    bw.Write(game.ToArray(karting));
                bw.Flush();
                return ms.ToArray();
            }
        }
    }
}
