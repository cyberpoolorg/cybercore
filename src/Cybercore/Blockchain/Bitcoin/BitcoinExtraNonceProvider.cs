using System;
using System.Security.Cryptography;
using System.Threading;

namespace Cybercore.Blockchain.Bitcoin
{
    public class BitcoinExtraNonceProvider : ExtraNonceProviderBase
    {
        public BitcoinExtraNonceProvider(string poolId, byte? clusterInstanceId) : base(poolId, 4, clusterInstanceId)
        {
        }
    }
}