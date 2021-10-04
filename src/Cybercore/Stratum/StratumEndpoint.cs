using System.Net;
using Cybercore.Configuration;

namespace Cybercore.Stratum
{
    public record StratumEndpoint(IPEndPoint IPEndPoint, PoolEndpoint PoolEndpoint);
}