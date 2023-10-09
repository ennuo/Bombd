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
    class BombGame
    {
        public Dictionary<int, string> Friends { get; set; } = new Dictionary<int, string>();
        public List<string> Guests { get; set; } = new List<string>();
        public string GameName { get; set; }
        public string DisplayName { get; set; }
        public Dictionary<string, string> GameAttributes { get; set; } = new Dictionary<string, string>();

        public byte[] ToArray()
        {
            using (var ms = new MemoryStream())
            using (var bw = new EndiannessAwareBinaryWriter(ms, EEndianness.Big))
            {
                bw.Write(0); // mNumPlayers / Friend count
                bw.Write(0); // mTimeSinceLastPlayerJoinedMS

                //bw.Write(Friends.Count);
                //foreach (var friend in Friends)
                //{
                //    bw.Write(friend.Key);
                //    bw.WriteStringMember(friend.Value);
                //}
                //bw.Write(Guests.Count);
                //foreach (var guest in Guests)
                //    bw.WriteStringMember(guest);

                bw.WriteStringMember(GameName);
                bw.WriteStringMember(DisplayName);

                bw.Write(GameAttributes.Count);
                foreach (var filter in GameAttributes)
                {
                    bw.WriteStringMember(filter.Key);
                    bw.WriteStringMember(filter.Value);
                }

                bw.Flush();
                return ms.ToArray();
            }
        }
    }
}
