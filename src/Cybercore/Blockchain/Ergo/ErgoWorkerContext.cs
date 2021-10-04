using System.Numerics;
using Cybercore.Mining;

namespace Cybercore.Blockchain.Ergo
{
    public class ErgoWorkerContext : WorkerContextBase
    {
        public string Miner { get; set; }
        public string Worker { get; set; }
        public string ExtraNonce1 { get; set; }
    }
}
