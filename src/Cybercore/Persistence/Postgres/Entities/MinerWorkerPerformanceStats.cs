using System;

namespace Cybercore.Persistence.Postgres.Entities
{
    public class MinerWorkerPerformanceStats
    {
        public long Id { get; set; }
        public string PoolId { get; set; }
        public string Miner { get; set; }
        public string Worker { get; set; }
        public double Hashrate { get; set; }
        public double SharesPerSecond { get; set; }
        public string IpAddress { get; set; }
        public decimal Balance { get; set; }
        public string Source { get; set; }
        public DateTime Created { get; set; }
        public int Partition { get; set; }
    }
}