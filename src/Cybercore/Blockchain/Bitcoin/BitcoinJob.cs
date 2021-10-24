using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Cybercore.Blockchain.Bitcoin.Configuration;
using Cybercore.Blockchain.Bitcoin.DaemonResponses;
using Cybercore.Configuration;
using Cybercore.Crypto;
using Cybercore.Extensions;
using Cybercore.Stratum;
using Cybercore.Time;
using Cybercore.Util;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;
using Contract = Cybercore.Contracts.Contract;
using Transaction = NBitcoin.Transaction;

namespace Cybercore.Blockchain.Bitcoin
{
    public class BitcoinJob
    {
        protected IHashAlgorithm blockHasher;
        protected IMasterClock clock;
        protected IHashAlgorithm coinbaseHasher;
        protected double shareMultiplier;
        protected int extraNoncePlaceHolderLength;
        protected IHashAlgorithm headerHasher;
        protected bool isPoS;
        protected string txComment;
        protected PayeeBlockTemplateExtra payeeParameters;
        protected Network network;
        protected IDestination poolAddressDestination;
        protected PoolConfig poolConfig;
        protected BitcoinTemplate coin;
        private BitcoinTemplate.BitcoinNetworkParams networkParams;
        protected readonly ConcurrentDictionary<string, bool> submissions = new(StringComparer.OrdinalIgnoreCase);
        protected uint256 blockTargetValue;
        protected byte[] coinbaseFinal;
        protected string coinbaseFinalHex;
        protected byte[] coinbaseInitial;
        protected string coinbaseInitialHex;
        protected string[] merkleBranchesHex;
        protected MerkleTree mt;
        protected object[] jobParams;
        protected string previousBlockHashReversedHex;
        protected Money rewardToPool;
        protected Transaction txOut;
        protected byte[] scriptSigFinalBytes;
        protected static byte[] sha256Empty = new byte[32];
        protected uint txVersion = 1u;
        protected static uint txInputCount = 1u;
        protected static uint txInPrevOutIndex = (uint)(Math.Pow(2, 32) - 1);
        protected static uint txInSequence;
        protected static uint txLockTime;

        protected virtual void BuildMerkleBranches()
        {
            var transactionHashes = BlockTemplate.Transactions
                .Select(tx => (tx.TxId ?? tx.Hash)
                    .HexToByteArray()
                    .ReverseInPlace())
                .ToArray();

            mt = new MerkleTree(transactionHashes);

            merkleBranchesHex = mt.Steps
                .Select(x => x.ToHexString())
                .ToArray();
        }

        protected virtual void BuildCoinbase()
        {
            var sigScriptInitial = GenerateScriptSigInitial();
            var sigScriptInitialBytes = sigScriptInitial.ToBytes();

            var sigScriptLength = (uint)(
                sigScriptInitial.Length +
                extraNoncePlaceHolderLength +
                scriptSigFinalBytes.Length);

            txOut = (coin.HasMasterNodes) ? CreateMasternodeOutputTransaction() : (coin.HasPayee ? CreatePayeeOutputTransaction() : CreateOutputTransaction());

            if (coin.HasCoinbasePayload)
            {
                txOut = CreatePayloadOutputTransaction();
            }

            using (var stream = new MemoryStream())
            {
                var bs = new BitcoinStream(stream, true);

                bs.ReadWrite(ref txVersion);

                if (isPoS && poolConfig.UseP2PK)
                {
                    var timestamp = BlockTemplate.CurTime;
                    bs.ReadWrite(ref timestamp);
                }

                bs.ReadWriteAsVarInt(ref txInputCount);
                bs.ReadWrite(ref sha256Empty);
                bs.ReadWrite(ref txInPrevOutIndex);
                bs.ReadWriteAsVarInt(ref sigScriptLength);
                bs.ReadWrite(ref sigScriptInitialBytes);

                coinbaseInitial = stream.ToArray();
                coinbaseInitialHex = coinbaseInitial.ToHexString();
            }

            using (var stream = new MemoryStream())
            {
                var bs = new BitcoinStream(stream, true);

                bs.ReadWrite(ref scriptSigFinalBytes);
                bs.ReadWrite(ref txInSequence);
                var txOutBytes = SerializeOutputTransaction(txOut);
                bs.ReadWrite(ref txOutBytes);
                bs.ReadWrite(ref txLockTime);
                AppendCoinbaseFinal(bs);

                coinbaseFinal = stream.ToArray();
                coinbaseFinalHex = coinbaseFinal.ToHexString();
            }
        }

        protected virtual void AppendCoinbaseFinal(BitcoinStream bs)
        {
            if (!string.IsNullOrEmpty(txComment))
            {
                var data = Encoding.ASCII.GetBytes(txComment);
                bs.ReadWriteAsVarString(ref data);
            }

            if (coin.HasMasterNodes && !string.IsNullOrEmpty(masterNodeParameters.CoinbasePayload))
            {
                var data = masterNodeParameters.CoinbasePayload.HexToByteArray();
                bs.ReadWriteAsVarString(ref data);
            }
        }

        protected virtual byte[] SerializeOutputTransaction(Transaction tx)
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

        protected virtual Script GenerateScriptSigInitial()
        {
            var now = ((DateTimeOffset)clock.Now).ToUnixTimeSeconds();

            var ops = new List<Op>();

            ops.Add(Op.GetPushOp(BlockTemplate.Height));

            if (!coin.CoinbaseIgnoreAuxFlags && !string.IsNullOrEmpty(BlockTemplate.CoinbaseAux?.Flags))
                ops.Add(Op.GetPushOp(BlockTemplate.CoinbaseAux.Flags.HexToByteArray()));

            ops.Add(Op.GetPushOp(now));

            ops.Add(Op.GetPushOp((uint)0));

            return new Script(ops);
        }

        protected virtual Transaction CreateOutputTransaction()
        {
            rewardToPool = new Money(BlockTemplate.CoinbaseValue, MoneyUnit.Satoshi);

            var tx = Transaction.Create(network);

            if (coin.HasFounderFee)
                rewardToPool = CreateFounderOutputs(tx, rewardToPool);

            tx.Outputs.Add(rewardToPool, poolAddressDestination);

            if (coin.HasCoinbaseDevReward)
                CreateCoinbaseDevRewardOutputs(tx);

            return tx;
        }

        protected virtual Transaction CreatePayeeOutputTransaction()
        {
            rewardToPool = new Money(BlockTemplate.CoinbaseValue, MoneyUnit.Satoshi);

            var tx = Transaction.Create(network);

            if (payeeParameters?.PayeeAmount > 0)
            {
                var payeeReward = new Money(payeeParameters.PayeeAmount.Value, MoneyUnit.Satoshi);
                rewardToPool -= payeeReward;

                tx.Outputs.Add(payeeReward, BitcoinUtils.AddressToDestination(payeeParameters.Payee, network));
            }

            tx.Outputs.Insert(0, new TxOut(rewardToPool, poolAddressDestination));

            return tx;
        }

        protected bool RegisterSubmit(string extraNonce1, string extraNonce2, string nTime, string nonce)
        {
            var key = new StringBuilder()
                .Append(extraNonce1)
                .Append(extraNonce2)
                .Append(nTime)
                .Append(nonce)
                .ToString();

            return submissions.TryAdd(key, true);
        }

        protected byte[] SerializeHeader(Span<byte> coinbaseHash, uint nTime, uint nonce, uint? versionMask, uint? versionBits)
        {
            var merkleRoot = mt.WithFirst(coinbaseHash.ToArray());

            var version = BlockTemplate.Version;

            if (versionMask.HasValue && versionBits.HasValue)
                version = (version & ~versionMask.Value) | (versionBits.Value & versionMask.Value);

#pragma warning disable 618
            var blockHeader = new BlockHeader
#pragma warning restore 618
            {
                Version = unchecked((int)version),
                Bits = new Target(Encoders.Hex.DecodeData(BlockTemplate.Bits)),
                HashPrevBlock = uint256.Parse(BlockTemplate.PreviousBlockhash),
                HashMerkleRoot = new uint256(merkleRoot),
                BlockTime = DateTimeOffset.FromUnixTimeSeconds(nTime),
                Nonce = nonce
            };

            return blockHeader.ToBytes();
        }

        protected virtual (Share Share, string BlockHex) ProcessShareInternal(
            StratumConnection worker, string extraNonce2, uint nTime, uint nonce, uint? versionBits)
        {
            var context = worker.ContextAs<BitcoinWorkerContext>();
            var extraNonce1 = context.ExtraNonce1;
            var coinbase = SerializeCoinbase(extraNonce1, extraNonce2);
            Span<byte> coinbaseHash = stackalloc byte[32];
            coinbaseHasher.Digest(coinbase, coinbaseHash);

            var headerBytes = SerializeHeader(coinbaseHash, nTime, nonce, context.VersionRollingMask, versionBits);
            Span<byte> headerHash = stackalloc byte[32];
            headerHasher.Digest(headerBytes, headerHash, (ulong)nTime, BlockTemplate, coin, networkParams);
            var headerValue = new uint256(headerHash);

            var shareDiff = (double)new BigRational(BitcoinConstants.Diff1, headerHash.ToBigInteger()) * shareMultiplier;
            var stratumDifficulty = context.Difficulty;
            var ratio = shareDiff / stratumDifficulty;

            var isBlockCandidate = headerValue <= blockTargetValue;

            if (!isBlockCandidate && ratio < 0.99)
            {
                if (context.VarDiff?.LastUpdate != null && context.PreviousDifficulty.HasValue)
                {
                    ratio = shareDiff / context.PreviousDifficulty.Value;

                    if (ratio < 0.99)
                        throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");

                    stratumDifficulty = context.PreviousDifficulty.Value;
                }

                else
                    throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");
            }

            var result = new Share
            {
                BlockHeight = BlockTemplate.Height,
                NetworkDifficulty = Difficulty,
                Difficulty = stratumDifficulty / shareMultiplier,
            };

            if (isBlockCandidate)
            {
                result.IsBlockCandidate = true;

                Span<byte> blockHash = stackalloc byte[32];
                blockHasher.Digest(headerBytes, blockHash, nTime);
                result.BlockHash = blockHash.ToHexString();

                var blockBytes = SerializeBlock(headerBytes, coinbase);
                var blockHex = blockBytes.ToHexString();

                return (result, blockHex);
            }

            return (result, null);
        }

        protected virtual byte[] SerializeCoinbase(string extraNonce1, string extraNonce2)
        {
            var extraNonce1Bytes = extraNonce1.HexToByteArray();
            var extraNonce2Bytes = extraNonce2.HexToByteArray();

            using (var stream = new MemoryStream())
            {
                stream.Write(coinbaseInitial);
                stream.Write(extraNonce1Bytes);
                stream.Write(extraNonce2Bytes);
                stream.Write(coinbaseFinal);

                return stream.ToArray();
            }
        }

        protected virtual byte[] SerializeBlock(byte[] header, byte[] coinbase)
        {
            var transactionCount = (uint)BlockTemplate.Transactions.Length + 1;
            var rawTransactionBuffer = BuildRawTransactionBuffer();

            using (var stream = new MemoryStream())
            {
                var bs = new BitcoinStream(stream, true);

                bs.ReadWrite(ref header);
                bs.ReadWriteAsVarInt(ref transactionCount);
                bs.ReadWrite(ref coinbase);
                bs.ReadWrite(ref rawTransactionBuffer);

                if (isPoS && poolConfig.UseP2PK)
                    bs.ReadWrite((byte)0);

                return stream.ToArray();
            }
        }

        protected virtual byte[] BuildRawTransactionBuffer()
        {
            using (var stream = new MemoryStream())
            {
                foreach (var tx in BlockTemplate.Transactions)
                {
                    var txRaw = tx.Data.HexToByteArray();
                    stream.Write(txRaw);
                }

                return stream.ToArray();
            }
        }

        #region Founder
        protected FounderBlockTemplateExtra founderParameters;
        protected virtual Money CreateFounderOutputs(Transaction tx, Money reward)
        {
            if (founderParameters.Founder != null)
            {
                Founder[] founders;
                if (founderParameters.Founder.Type == JTokenType.Array)
                    founders = founderParameters.Founder.ToObject<Founder[]>();
                else
                    founders = new[] { founderParameters.Founder.ToObject<Founder>() };

                foreach (var Founder in founders)
                {
                    if (!string.IsNullOrEmpty(Founder.Payee))
                    {
                        var payeeAddress = BitcoinUtils.AddressToDestination(Founder.Payee, network);
                        var payeeReward = Founder.Amount;
                        reward -= payeeReward;
                        rewardToPool -= payeeReward;
                        tx.Outputs.Add(payeeReward, payeeAddress);
                    }
                }
            }
            return reward;
        }
        #endregion // Founder

        #region Masternodes
        protected MasterNodeBlockTemplateExtra masterNodeParameters;

        protected virtual Transaction CreateMasternodeOutputTransaction()
        {
            var blockReward = new Money(BlockTemplate.CoinbaseValue, MoneyUnit.Satoshi);
            rewardToPool = new Money(BlockTemplate.CoinbaseValue, MoneyUnit.Satoshi);
            var tx = Transaction.Create(network);
            rewardToPool = CreateMasternodeOutputs(tx, blockReward);

            if (coin.HasFounderFee)
                rewardToPool = CreateFounderOutputs(tx, rewardToPool);

            tx.Outputs.Insert(0, new TxOut(rewardToPool, poolAddressDestination));

            return tx;
        }

        protected virtual Money CreateMasternodeOutputs(Transaction tx, Money reward)
        {
            if (masterNodeParameters.Masternode != null)
            {
                Masternode[] masternodes;
                if (masterNodeParameters.Masternode.Type == JTokenType.Array)
                    masternodes = masterNodeParameters.Masternode.ToObject<Masternode[]>();
                else
                    masternodes = new[] { masterNodeParameters.Masternode.ToObject<Masternode>() };

                foreach (var masterNode in masternodes)
                {
                    if (!string.IsNullOrEmpty(masterNode.Payee))
                    {
                        var payeeDestination = BitcoinUtils.AddressToDestination(masterNode.Payee, network);
                        var payeeReward = masterNode.Amount;

                        if (!(poolConfig.Template.Symbol == "IDX" || poolConfig.Template.Symbol == "VGC" || poolConfig.Template.Symbol == "SHRX" || poolConfig.Template.Symbol == "XZC" || poolConfig.Template.Symbol == "RTM"))
                        {
                            reward -= payeeReward;
                            rewardToPool -= payeeReward;
                        }

                        tx.Outputs.Add(payeeReward, payeeDestination);
                    }
                }
            }

            if (masterNodeParameters.SuperBlocks != null && masterNodeParameters.SuperBlocks.Length > 0)
            {
                foreach (var superBlock in masterNodeParameters.SuperBlocks)
                {
                    var payeeAddress = BitcoinUtils.AddressToDestination(superBlock.Payee, network);
                    var payeeReward = superBlock.Amount;

                    reward -= payeeReward;
                    rewardToPool -= payeeReward;

                    tx.Outputs.Add(payeeReward, payeeAddress);
                }
            }

            if (!coin.HasPayee && !string.IsNullOrEmpty(masterNodeParameters.Payee))
            {
                var payeeAddress = BitcoinUtils.AddressToDestination(masterNodeParameters.Payee, network);
                var payeeReward = masterNodeParameters.PayeeAmount;

                if (!(poolConfig.Template.Symbol == "IDX" || poolConfig.Template.Symbol == "VGC" || poolConfig.Template.Symbol == "SHRX" || poolConfig.Template.Symbol == "XZC" || poolConfig.Template.Symbol == "RTM"))
                {
                    reward -= payeeReward;
                    rewardToPool -= payeeReward;
                }

                tx.Outputs.Add(payeeReward, payeeAddress);
            }
            return reward;
        }
        #endregion // Masternodes

        #region DevaultCoinbasePayload
        protected CoinbasePayloadBlockTemplateExtra coinbasepayloadParameters;

        protected virtual Transaction CreatePayloadOutputTransaction()
        {
            var blockReward = new Money(BlockTemplate.CoinbaseValue, MoneyUnit.Satoshi);
            var tx = Transaction.Create(network);
            tx.Outputs.Insert(0, new TxOut(blockReward, poolAddressDestination));
            CreatePayloadOutputs(tx, rewardToPool);
            return tx;
        }

        protected virtual void CreatePayloadOutputs(Transaction tx, Money reward)
        {
            if (coinbasepayloadParameters.CoinbasePayload != null)
            {
                CoinbasePayload[] coinbasepayloads;
                if (coinbasepayloadParameters.CoinbasePayload.Type == JTokenType.Array)
                    coinbasepayloads = coinbasepayloadParameters.CoinbasePayload.ToObject<CoinbasePayload[]>();
                else
                    coinbasepayloads = new[] { coinbasepayloadParameters.CoinbasePayload.ToObject<CoinbasePayload>() };

                foreach (var CoinbasePayee in coinbasepayloads)
                {
                    if (!string.IsNullOrEmpty(CoinbasePayee.Payee))
                    {
                        var payeeAddress = BitcoinUtils.CashAddrToDestination(CoinbasePayee.Payee, network, true);
                        var payeeReward = CoinbasePayee.Amount;

                        tx.Outputs.Add(payeeReward, payeeAddress);
                    }
                }
            }
        }
        #endregion // DevaultCoinbasePayload

        #region CoinbaseDevReward
        protected CoinbaseDevRewardTemplateExtra CoinbaseDevRewardParams;
        protected virtual void CreateCoinbaseDevRewardOutputs(Transaction tx)
        {
            if (CoinbaseDevRewardParams.CoinbaseDevReward != null)
            {
                CoinbaseDevReward[] CBRewards;
                CBRewards = new[] { CoinbaseDevRewardParams.CoinbaseDevReward.ToObject<CoinbaseDevReward>() };

                foreach (var CBReward in CBRewards)
                {
                    if (!string.IsNullOrEmpty(CBReward.Payee))
                    {
                        var payeeAddress = BitcoinUtils.AddressToDestination(CBReward.Payee, network);
                        var payeeReward = CBReward.Value;

                        tx.Outputs.Add(payeeReward, payeeAddress);
                    }
                }
            }
        }
        #endregion // CoinbaseDevReward

        #region API-Surface
        public BlockTemplate BlockTemplate { get; protected set; }
        public double Difficulty { get; protected set; }

        public string JobId { get; protected set; }

        public void Init(BlockTemplate blockTemplate, string jobId,
            PoolConfig poolConfig, BitcoinPoolConfigExtra extraPoolConfig,
            ClusterConfig clusterConfig, IMasterClock clock,
            IDestination poolAddressDestination, Network network,
            bool isPoS, double shareMultiplier, IHashAlgorithm coinbaseHasher,
            IHashAlgorithm headerHasher, IHashAlgorithm blockHasher)
        {
            Contract.RequiresNonNull(blockTemplate, nameof(blockTemplate));
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(poolAddressDestination, nameof(poolAddressDestination));
            Contract.RequiresNonNull(coinbaseHasher, nameof(coinbaseHasher));
            Contract.RequiresNonNull(headerHasher, nameof(headerHasher));
            Contract.RequiresNonNull(blockHasher, nameof(blockHasher));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId), $"{nameof(jobId)} must not be empty");

            this.poolConfig = poolConfig;
            coin = poolConfig.Template.As<BitcoinTemplate>();
            networkParams = coin.GetNetwork(network.ChainName);
            txVersion = coin.CoinbaseTxVersion;
            this.network = network;
            this.clock = clock;
            this.poolAddressDestination = poolAddressDestination;
            BlockTemplate = blockTemplate;
            JobId = jobId;
            var coinbaseString = !string.IsNullOrEmpty(clusterConfig.PaymentProcessing?.CoinbaseString) ? clusterConfig.PaymentProcessing?.CoinbaseString.Trim() : "Cybercore";
            scriptSigFinalBytes = new Script(Op.GetPushOp(Encoding.UTF8.GetBytes(coinbaseString))).ToBytes();
            Difficulty = new Target(System.Numerics.BigInteger.Parse(BlockTemplate.Target, NumberStyles.HexNumber)).Difficulty;
            extraNoncePlaceHolderLength = BitcoinConstants.ExtranoncePlaceHolderLength;
            this.isPoS = isPoS;
            this.shareMultiplier = shareMultiplier;

            txComment = !string.IsNullOrEmpty(extraPoolConfig?.CoinbaseTxComment) ?
                extraPoolConfig.CoinbaseTxComment : coin.CoinbaseTxComment;

            if (coin.HasMasterNodes)
            {
                masterNodeParameters = BlockTemplate.Extra.SafeExtensionDataAs<MasterNodeBlockTemplateExtra>();

                if (!string.IsNullOrEmpty(masterNodeParameters.CoinbasePayload))
                {
                    txVersion = 3;
                    var txType = 5;
                    txVersion += ((uint)(txType << 16));
                }
            }

            if (coin.HasCoinbasePayload)
                coinbasepayloadParameters = BlockTemplate.Extra.SafeExtensionDataAs<CoinbasePayloadBlockTemplateExtra>();

            if (coin.HasFounderFee)
                founderParameters = BlockTemplate.Extra.SafeExtensionDataAs<FounderBlockTemplateExtra>();

            if (coin.HasCoinbaseDevReward)
                CoinbaseDevRewardParams = BlockTemplate.Extra.SafeExtensionDataAs<CoinbaseDevRewardTemplateExtra>();

            if (coin.HasPayee)
                payeeParameters = BlockTemplate.Extra.SafeExtensionDataAs<PayeeBlockTemplateExtra>();

            this.coinbaseHasher = coinbaseHasher;
            this.headerHasher = headerHasher;
            this.blockHasher = blockHasher;

            if (!string.IsNullOrEmpty(BlockTemplate.Target))
                blockTargetValue = new uint256(BlockTemplate.Target);
            else
            {
                var tmp = new Target(BlockTemplate.Bits.HexToByteArray());
                blockTargetValue = tmp.ToUInt256();
            }

            previousBlockHashReversedHex = BlockTemplate.PreviousBlockhash
                .HexToByteArray()
                .ReverseByteOrder()
                .ToHexString();

            BuildMerkleBranches();
            BuildCoinbase();

            jobParams = new object[]
            {
                JobId,
                previousBlockHashReversedHex,
                coinbaseInitialHex,
                coinbaseFinalHex,
                merkleBranchesHex,
                BlockTemplate.Version.ToStringHex8(),
                BlockTemplate.Bits,
                BlockTemplate.CurTime.ToStringHex8(),
                false
            };
        }

        public object GetJobParams(bool isNew)
        {
            jobParams[^1] = isNew;
            return jobParams;
        }

        public virtual (Share Share, string BlockHex) ProcessShare(StratumConnection worker,
            string extraNonce2, string nTime, string nonce, string versionBits = null)
        {
            Contract.RequiresNonNull(worker, nameof(worker));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(extraNonce2), $"{nameof(extraNonce2)} must not be empty");
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nTime), $"{nameof(nTime)} must not be empty");
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nonce), $"{nameof(nonce)} must not be empty");

            var context = worker.ContextAs<BitcoinWorkerContext>();

            if (nTime.Length != 8)
                throw new StratumException(StratumError.Other, "incorrect size of ntime");

            var nTimeInt = uint.Parse(nTime, NumberStyles.HexNumber);
            if (nTimeInt < BlockTemplate.CurTime || nTimeInt > ((DateTimeOffset)clock.Now).ToUnixTimeSeconds() + 7200)
                throw new StratumException(StratumError.Other, "ntime out of range");

            if (nonce.Length != 8)
                throw new StratumException(StratumError.Other, "incorrect size of nonce");

            var nonceInt = uint.Parse(nonce, NumberStyles.HexNumber);

            uint versionBitsInt = 0;

            if (context.VersionRollingMask.HasValue && versionBits != null)
            {
                versionBitsInt = uint.Parse(versionBits, NumberStyles.HexNumber);

                if ((versionBitsInt & ~context.VersionRollingMask.Value) != 0)
                    throw new StratumException(StratumError.Other, "rolling-version mask violation");
            }

            if (!RegisterSubmit(context.ExtraNonce1, extraNonce2, nTime, nonce))
                throw new StratumException(StratumError.DuplicateShare, "duplicate share");

            return ProcessShareInternal(worker, extraNonce2, nTimeInt, nonceInt, versionBitsInt);
        }

        #endregion // API-Surface
    }
}