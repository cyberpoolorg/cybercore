using System.Threading;
using System.Threading.Tasks;
using Cybercore.Blockchain;
using Cybercore.Configuration;

namespace Cybercore.Mining
{
    public interface IMiningPool
    {
        PoolConfig Config { get; }
        PoolStats PoolStats { get; }
        BlockchainStats NetworkStats { get; }
        double ShareMultiplier { get; }
        void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig);
        double HashrateFromShares(double shares, double interval);
        Task RunAsync(CancellationToken ct);
    }
}