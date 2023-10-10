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
    class BombServiceList
    {
        public Dictionary<string, List<BombService>> ServiceList { get; private set; } = new Dictionary<string, List<BombService>>();

        public BombServiceList() { }
        public BombServiceList(BombService service) => Add(service);
        public BombServiceList(params BombService[] services) => AddRange(services);
        public BombServiceList(IEnumerable<BombService> services) => AddRange(services.ToArray());

        public void Add(BombService service)
        {
            if (!ServiceList.ContainsKey(service.Name))
                ServiceList.Add(service.Name, new List<BombService>());
            ServiceList[service.Name].Add(service);
        }

        public void AddRange(params BombService[] services)
        {
            foreach (var service in services)
                Add(service);
        }

        public byte[] ToArray()
        {
            using (var ms = new MemoryStream())
            using (var bw = new EndiannessAwareBinaryWriter(ms, EEndianness.Big))
            {
                // The game reads 8 ClusterServices
                foreach (var serviceType in ServiceList)
                {
                    bw.WriteStringMember(serviceType.Key); // mServiceName
                    bw.Write(serviceType.Value.Count); // mNumServices
                    
                    // ServiceInstance
                    foreach (var service in serviceType.Value) // Limit of 3
                    {
                        bw.WriteStringMember(service.IP); // mHostName
                        bw.WriteStringMember(service.Uuid); // mServerUUID
                        bw.WriteStringMember(service.Port); // mClientPort
                        bw.WriteStringMember(service.Protocol); // mProtocol
                        bw.Write(0); // mConnectOrder
                        bw.Write(1); // mKey
                    }
                }
                
                bw.Write(new byte[255]);    //HACK, the game uses a fixed amount of results, with unused slots being null, implement this
                bw.Flush();
                File.WriteAllBytes("ServiceList.bin", ms.ToArray());
                return ms.ToArray();
            }
        }
    }
}
