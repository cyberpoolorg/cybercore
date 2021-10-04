using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Cybercore.Extensions;

namespace Cybercore.Blockchain.Ethereum
{
    public class EthereumExtraNonceProvider : ExtraNonceProviderBase
    {
        public EthereumExtraNonceProvider(string poolId, byte? clusterInstanceId) : base(poolId, 2, clusterInstanceId)
        {
        }
    }
}