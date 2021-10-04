using Cybercore.Mining;

namespace Cybercore.Blockchain.Ethereum
{
    public class EthereumWorkerContext : WorkerContextBase
    {
        public string Miner { get; set; }
        public string Worker { get; set; }
        public bool IsInitialWorkSent { get; set; } = false;
        public string ExtraNonce1 { get; set; }
    }
}