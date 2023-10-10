using System;
using System.Runtime.CompilerServices;

namespace BombServerEmu_MNR.Src.Protocols.Clients
{
    public enum AckState
    {
        None = 0,
        Waiting = 1,
        Received = 2
    }
    
    public class AckPacketRecord
    {
        public byte[] Datagram { get; set; }
        public EBombPacketType Protocol { get; set; }
        public int SequenceNumber { get; set; }
        public DateTime ResendTime { get; set; }
        public AckPacketRecord State { get; set; }
        private int TimesResent { get; set; }
    }
}