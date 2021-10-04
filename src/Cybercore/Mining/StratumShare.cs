using Cybercore.Blockchain;
using Cybercore.Stratum;

namespace Cybercore.Mining
{
    public record StratumShare(StratumConnection Connection, Share Share);
}