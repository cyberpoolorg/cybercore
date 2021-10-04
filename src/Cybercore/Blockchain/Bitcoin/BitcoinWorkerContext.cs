using Cybercore.Mining;

namespace Cybercore.Blockchain.Bitcoin
{
    public class BitcoinWorkerContext : WorkerContextBase
    {
        public string Miner { get; set; }
        public string Worker { get; set; }
        public string ExtraNonce1 { get; set; }
        public uint? VersionRollingMask { get; internal set; }
    }
}