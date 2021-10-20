using System;

namespace Cybercore.Mining
{
    public class PoolStats
    {
        public DateTime? LastPoolBlockTime { get; set; }
        public int ConnectedMiners { get; set; }
        public int ConnectedWorkers { get; set; }
        public ulong PoolHashrate { get; set; }
        public double SharesPerSecond { get; set; }
        public double RoundShares { get; set; }
        public double RoundEffort { get; set; }
    }
}