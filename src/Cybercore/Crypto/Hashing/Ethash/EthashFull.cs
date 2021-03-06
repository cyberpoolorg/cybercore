using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cybercore.Blockchain.Ethereum;
using Cybercore.Contracts;
using NLog;

namespace Cybercore.Crypto.Hashing.Ethash
{
    public class EthashFull : IDisposable
    {
        public EthashFull(int numCaches, string dagDir)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(dagDir), $"{nameof(dagDir)} must not be empty");

            this.numCaches = numCaches;
            this.dagDir = dagDir;
        }

        private int numCaches;
        private readonly object cacheLock = new();
        private readonly Dictionary<ulong, Dag> caches = new();
        private Dag future;
        private readonly string dagDir;

        public void Dispose()
        {
            foreach (var value in caches.Values)
                value.Dispose();
        }

        public async Task<Dag> GetDagAsync(ulong block, ILogger logger, CancellationToken ct)
        {
            var epoch = block / EthereumConstants.EpochLength;
            Dag result;

            lock (cacheLock)
            {
                if (numCaches == 0)
                    numCaches = 3;

                if (!caches.TryGetValue(epoch, out result))
                {
                    while (caches.Count >= numCaches)
                    {
                        var toEvict = caches.Values.OrderBy(x => x.LastUsed).First();
                        var key = caches.First(pair => pair.Value == toEvict).Key;
                        var epochToEvict = toEvict.Epoch;

                        logger.Info(() => $"Evicting DAG for epoch {epochToEvict} in favour of epoch {epoch}");
                        toEvict.Dispose();
                        caches.Remove(key);
                    }

                    if (future != null && future.Epoch == epoch)
                    {
                        logger.Debug(() => $"Using pre-generated DAG for epoch {epoch}");

                        result = future;
                        future = null;
                    }

                    else
                    {
                        logger.Info(() => $"No pre-generated DAG available, creating new for epoch {epoch}");
                        result = new Dag(epoch);
                    }

                    caches[epoch] = result;
                }

                else if (future == null || future.Epoch <= epoch)
                {
                    logger.Info(() => $"Pre-generating DAG for epoch {epoch + 1}");
                    future = new Dag(epoch + 1);

#pragma warning disable 4014
                    future.GenerateAsync(dagDir, logger, ct);
#pragma warning restore 4014
                }

                result.LastUsed = DateTime.Now;
            }

            await result.GenerateAsync(dagDir, logger, ct);
            return result;
        }
    }
}