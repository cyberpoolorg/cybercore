using System;
using System.Linq;
using Cybercore.Extensions;
using NBitcoin;
using Transaction = NBitcoin.Transaction;

namespace Cybercore.Blockchain.Equihash.Custom.Minexcoin
{
    public class MinexcoinJob : EquihashJob
    {
        private static readonly Script bankScript = new("2103ae6efe9458f1d3bdd9a458b1970eabbdf9fcb1357e0dff2744a777ff43c391eeac".HexToByteArray());
        private const decimal BlockReward = 250000000m;

        protected override Transaction CreateOutputTransaction()
        {
            var txFees = BlockTemplate.Transactions.Sum(x => x.Fee);
            rewardToPool = new Money(BlockReward + txFees, MoneyUnit.Satoshi);

            var bankReward = ComputeBankReward(BlockTemplate.Height, rewardToPool);
            rewardToPool -= bankReward;

            var tx = Transaction.Create(network);

            tx.Outputs.Add(rewardToPool, poolAddressDestination);

            tx.Outputs.Add(bankReward, bankScript);

            tx.Inputs.Add(TxIn.CreateCoinbase((int)BlockTemplate.Height));

            return tx;
        }

        private Money ComputeBankReward(uint blockHeight, Money totalReward)
        {
            if (blockHeight <= 4500000)
            {
                return new Money(Math.Floor((decimal)totalReward.Satoshi / 10) * (2.0m + Math.Floor(((decimal)blockHeight - 1) / 900000)), MoneyUnit.Satoshi);
            }

            return new Money(Math.Floor((decimal)totalReward.Satoshi / 10) * 7, MoneyUnit.Satoshi);
        }
    }
}