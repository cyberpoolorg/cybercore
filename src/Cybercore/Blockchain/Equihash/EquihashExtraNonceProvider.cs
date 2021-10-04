using System;
using System.Linq;
using System.Threading;
using Cybercore.Extensions;

namespace Cybercore.Blockchain.Equihash
{
    public class EquihashExtraNonceProvider : ExtraNonceProviderBase
    {
        public EquihashExtraNonceProvider(string poolId, byte? clusterInstanceId) : base(poolId, 4, clusterInstanceId)
        {
        }
    }
}