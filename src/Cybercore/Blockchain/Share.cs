using System;
using ProtoBuf;

namespace Cybercore.Blockchain
{
    [ProtoContract]
    public class Share
    {
        [ProtoMember(1)]
        public string PoolId { get; set; }

        [ProtoMember(2)]
        public string Miner { get; set; }

        [ProtoMember(3)]
        public string Worker { get; set; }

        [ProtoMember(5)]
        public string UserAgent { get; set; }

        [ProtoMember(6)]
        public string IpAddress { get; set; }

        [ProtoMember(7)]
        public string Source { get; set; }

        [ProtoMember(8)]
        public double Difficulty { get; set; }

        [ProtoMember(9)]
        public long BlockHeight { get; set; }
        public decimal BlockReward { get; set; }

        [ProtoMember(10)]
        public double BlockRewardDouble { get; set; }

        [ProtoMember(11)]
        public string BlockHash { get; set; }

        [ProtoMember(12)]
        public bool IsBlockCandidate { get; set; }

        [ProtoMember(13)]
        public string TransactionConfirmationData { get; set; }

        [ProtoMember(14)]
        public double NetworkDifficulty { get; set; }

        [ProtoMember(15)]
        public DateTime Created { get; set; }
    }
}