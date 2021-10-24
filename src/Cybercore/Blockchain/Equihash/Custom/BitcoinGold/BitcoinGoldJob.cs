using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cybercore.Blockchain.Bitcoin;
using Cybercore.Blockchain.Equihash.DaemonResponses;
using Cybercore.Configuration;
using Cybercore.Contracts;
using Cybercore.Crypto.Hashing.Equihash;
using Cybercore.Extensions;
using Cybercore.Time;
using Cybercore.Util;
using NBitcoin;
using NBitcoin.DataEncoders;
using Transaction = NBitcoin.Transaction;

namespace Cybercore.Blockchain.Equihash.Custom.BitcoinGold
{
    public class BitcoinGoldJob : EquihashJob
    {
        protected uint coinbaseIndex = 4294967295u;
        protected uint coinbaseSequence = 4294967295u;
        private static uint txInputCount = 1u;
        private static uint txLockTime;

        protected override Transaction CreateOutputTransaction()
        {
            rewardToPool = new Money(BlockTemplate.CoinbaseValue, MoneyUnit.Satoshi);

            var tx = Transaction.Create(network);

            tx.Outputs.Add(rewardToPool, poolAddressDestination);

            tx.Inputs.Add(TxIn.CreateCoinbase((int)BlockTemplate.Height));

            return tx;
        }

        protected override void BuildCoinbase()
        {
            var script = TxIn.CreateCoinbase((int)BlockTemplate.Height).ScriptSig;

            txOut = CreateOutputTransaction();

            using (var stream = new MemoryStream())
            {
                var bs = new BitcoinStream(stream, true);

                bs.ReadWrite(ref txVersion);
                bs.ReadWriteAsVarInt(ref txInputCount);
                bs.ReadWrite(ref sha256Empty);
                bs.ReadWrite(ref coinbaseIndex);
                bs.ReadWrite(ref script);
                bs.ReadWrite(ref coinbaseSequence);

                var txOutBytes = SerializeOutputTransaction(txOut);
                bs.ReadWrite(ref txOutBytes);

                bs.ReadWrite(ref txLockTime);

                coinbaseInitial = stream.ToArray();
                coinbaseInitialHash = new byte[32];
                sha256D.Digest(coinbaseInitial, coinbaseInitialHash);
            }
        }

        private byte[] SerializeOutputTransaction(Transaction tx)
        {
            var withDefaultWitnessCommitment = !string.IsNullOrEmpty(BlockTemplate.DefaultWitnessCommitment);

            var outputCount = (uint)tx.Outputs.Count;
            if (withDefaultWitnessCommitment)
                outputCount++;

            using (var stream = new MemoryStream())
            {
                var bs = new BitcoinStream(stream, true);

                bs.ReadWriteAsVarInt(ref outputCount);

                long amount;
                byte[] raw;
                uint rawLength;

                if (withDefaultWitnessCommitment)
                {
                    amount = 0;
                    raw = BlockTemplate.DefaultWitnessCommitment.HexToByteArray();
                    rawLength = (uint)raw.Length;

                    bs.ReadWrite(ref amount);
                    bs.ReadWriteAsVarInt(ref rawLength);
                    bs.ReadWrite(ref raw);
                }

                foreach (var output in tx.Outputs)
                {
                    amount = output.Value.Satoshi;
                    var outScript = output.ScriptPubKey;
                    raw = outScript.ToBytes(true);
                    rawLength = (uint)raw.Length;

                    bs.ReadWrite(ref amount);
                    bs.ReadWriteAsVarInt(ref rawLength);
                    bs.ReadWrite(ref raw);
                }

                return stream.ToArray();
            }
        }

        protected override byte[] SerializeHeader(uint nTime, string nonce)
        {
            var heightAndReserved = new byte[32];
            BitConverter.TryWriteBytes(heightAndReserved, BlockTemplate.Height);

            var blockHeader = new EquihashBlockHeader
            {
                Version = (int)BlockTemplate.Version,
                Bits = new Target(Encoders.Hex.DecodeData(BlockTemplate.Bits)),
                HashPrevBlock = uint256.Parse(BlockTemplate.PreviousBlockhash),
                HashMerkleRoot = new uint256(merkleRoot),
                HashReserved = heightAndReserved,
                NTime = nTime,
                Nonce = nonce
            };

            return blockHeader.ToBytes();
        }

        public override void Init(EquihashBlockTemplate blockTemplate, string jobId,
            PoolConfig poolConfig, ClusterConfig clusterConfig, IMasterClock clock,
            IDestination poolAddressDestination, Network network,
            EquihashSolver solver)
        {
            Contract.RequiresNonNull(blockTemplate, nameof(blockTemplate));
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(poolAddressDestination, nameof(poolAddressDestination));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId), $"{nameof(jobId)} must not be empty");

            this.clock = clock;
            this.poolAddressDestination = poolAddressDestination;
            coin = poolConfig.Template.As<EquihashCoinTemplate>();
            this.network = network;
            var equihashTemplate = poolConfig.Template.As<EquihashCoinTemplate>();
            networkParams = coin.GetNetwork(network.ChainName);
            BlockTemplate = blockTemplate;
            JobId = jobId;
            Difficulty = (double)new BigRational(networkParams.Diff1BValue, BlockTemplate.Target.HexToReverseByteArray().AsSpan().ToBigInteger());

            this.solver = solver;

            if (!string.IsNullOrEmpty(BlockTemplate.Target))
                blockTargetValue = new uint256(BlockTemplate.Target);
            else
            {
                var tmp = new Target(BlockTemplate.Bits.HexToByteArray());
                blockTargetValue = tmp.ToUInt256();
            }

            previousBlockHashReversedHex = BlockTemplate.PreviousBlockhash
                .HexToByteArray()
                .ReverseInPlace()
                .ToHexString();

            BuildCoinbase();

            var txHashes = new List<uint256> { new(coinbaseInitialHash) };
            txHashes.AddRange(BlockTemplate.Transactions.Select(tx => new uint256(tx.TxId.HexToReverseByteArray())));

            merkleRoot = MerkleNode.GetRoot(txHashes).Hash.ToBytes().ReverseInPlace();
            merkleRootReversed = merkleRoot.ReverseInPlace();
            merkleRootReversedHex = merkleRootReversed.ToHexString();

            jobParams = new object[]
            {
                JobId,
                BlockTemplate.Version.ReverseByteOrder().ToStringHex8(),
                previousBlockHashReversedHex,
                merkleRootReversedHex,
                BlockTemplate.Height.ReverseByteOrder().ToStringHex8() + sha256Empty.Take(28).ToHexString(),
                BlockTemplate.CurTime.ReverseByteOrder().ToStringHex8(),
                BlockTemplate.Bits.HexToReverseByteArray().ToHexString(),
                false
            };
        }
    }
}