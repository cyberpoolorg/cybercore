using System.Collections.Generic;
using System.Linq;
using Cybercore.Mining;

namespace Cybercore.Blockchain.Cryptonote
{
    public class CryptonoteWorkerContext : WorkerContextBase
    {
        public string Miner { get; set; }
        public string Worker { get; set; }

        private List<CryptonoteWorkerJob> validJobs { get; } = new();

        public void AddJob(CryptonoteWorkerJob job)
        {
            validJobs.Insert(0, job);

            while (validJobs.Count > 4)
                validJobs.RemoveAt(validJobs.Count - 1);
        }

        public CryptonoteWorkerJob FindJob(string jobId)
        {
            return validJobs.FirstOrDefault(x => x.Id == jobId);
        }
    }
}