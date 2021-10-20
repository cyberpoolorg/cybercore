using System;

namespace Cybercore.Persistence.Model
{
    public class PoolStats
    {
        public long Id { get; set; }
        public string PoolId { get; set; }
        public int ConnectedMiners { get; set; }
        public int ConnectedWorkers { get; set; }
        public float PoolHashrate { get; set; }
        public double NetworkHashrate { get; set; }
        public double NetworkDifficulty { get; set; }
        public DateTime? LastNetworkBlockTime { get; set; }
        public long BlockHeight { get; set; }
        public long BlockReward { get; set; }
        public int ConnectedPeers { get; set; }
        public double SharesPerSecond { get; set; }
        public double RoundShares { get; set; }
        public double RoundEffort { get; set; }
        public DateTime? LastPoolBlockTime { get; set; }
        public DateTime Created { get; set; }
    }
}