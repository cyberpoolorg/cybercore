using Autofac;
using AutoMapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Cybercore.Blockchain.Ethereum.Configuration;
using Cybercore.Blockchain.Ethereum.DaemonRequests;
using Cybercore.Blockchain.Ethereum.DaemonResponses;
using Cybercore.Configuration;
using Cybercore.DaemonInterface;
using Cybercore.Extensions;
using Cybercore.JsonRpc;
using Cybercore.Messaging;
using Cybercore.Mining;
using Cybercore.Payments;
using Cybercore.Persistence;
using Cybercore.Persistence.Model;
using Cybercore.Persistence.Repositories;
using Cybercore.Time;
using Cybercore.Util;
using Newtonsoft.Json;
using Block = Cybercore.Persistence.Model.Block;
using Contract = Cybercore.Contracts.Contract;
using EC = Cybercore.Blockchain.Ethereum.EthCommands;

namespace Cybercore.Blockchain.Ethereum
{
    [CoinFamily(CoinFamily.Ethereum)]
    public class EthereumPayoutHandler : PayoutHandlerBase,
        IPayoutHandler
    {
        public EthereumPayoutHandler(
            IComponentContext ctx,
            IConnectionFactory cf,
            IMapper mapper,
            IShareRepository shareRepo,
            IBlockRepository blockRepo,
            IBalanceRepository balanceRepo,
            IPaymentRepository paymentRepo,
            IMasterClock clock,
            IMessageBus messageBus) :
            base(cf, mapper, shareRepo, blockRepo, balanceRepo, paymentRepo, clock, messageBus)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(balanceRepo, nameof(balanceRepo));
            Contract.RequiresNonNull(paymentRepo, nameof(paymentRepo));

            this.ctx = ctx;
        }

        private readonly IComponentContext ctx;
        private DaemonClient daemon;
        private EthereumNetworkType networkType;
        private GethChainType chainType;
        private const int BlockSearchOffset = 50;
        private EthereumPoolConfigExtra extraPoolConfig;
        private EthereumPoolPaymentProcessingConfigExtra extraConfig;

        protected override string LogCategory => "Ethereum Payout Handler";

        #region IPayoutHandler

        public async Task ConfigureAsync(ClusterConfig clusterConfig, PoolConfig poolConfig, CancellationToken ct)
        {
            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;
            extraPoolConfig = poolConfig.Extra.SafeExtensionDataAs<EthereumPoolConfigExtra>();
            extraConfig = poolConfig.PaymentProcessing.Extra.SafeExtensionDataAs<EthereumPoolPaymentProcessingConfigExtra>();

            logger = LogUtil.GetPoolScopedLogger(typeof(EthereumPayoutHandler), poolConfig);

            var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

            var daemonEndpoints = poolConfig.Daemons
                .Where(x => string.IsNullOrEmpty(x.Category))
                .ToArray();

            daemon = new DaemonClient(jsonSerializerSettings, messageBus, clusterConfig.ClusterName ?? poolConfig.PoolName, poolConfig.Id);
            daemon.Configure(daemonEndpoints);

            await DetectChainAsync(ct);
        }

        public async Task<Block[]> ClassifyBlocksAsync(IMiningPool pool, Block[] blocks, CancellationToken ct)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(blocks, nameof(blocks));

            var coin = poolConfig.Template.As<EthereumCoinTemplate>();
            var pageSize = 100;
            var pageCount = (int)Math.Ceiling(blocks.Length / (double)pageSize);
            var blockCache = new Dictionary<long, DaemonResponses.Block>();
            var result = new List<Block>();

            for (var i = 0; i < pageCount; i++)
            {
                var page = blocks
                    .Skip(i * pageSize)
                    .Take(pageSize)
                    .ToArray();

                var latestBlockResponses = await daemon.ExecuteCmdAllAsync<DaemonResponses.Block>(logger, EC.GetBlockByNumber, ct, new[] { (object)"latest", true });
                var latestBlockHeight = latestBlockResponses.First(x => x.Error == null && x.Response?.Height != null).Response.Height.Value;
                var blockInfos = await FetchBlocks(blockCache, ct, page.Select(block => (long)block.BlockHeight).ToArray());

                for (var j = 0; j < blockInfos.Length; j++)
                {
                    var blockInfo = blockInfos[j];
                    var block = page[j];

                    block.ConfirmationProgress = Math.Min(1.0d, (double)(latestBlockHeight - block.BlockHeight) / EthereumConstants.MinConfimations);
                    result.Add(block);

                    messageBus.NotifyBlockConfirmationProgress(poolConfig.Id, block, coin);

                    if (string.Equals(blockInfo.Miner, poolConfig.Address, StringComparison.OrdinalIgnoreCase))
                    {
                        if (latestBlockHeight - block.BlockHeight >= EthereumConstants.MinConfimations)
                        {
                            var blockHashResponses = await daemon.ExecuteCmdAllAsync<DaemonResponses.Block>(logger, EC.GetBlockByNumber, ct, new[] { (object)block.BlockHeight.ToStringHexWithPrefix(), true });
                            var blockHash = blockHashResponses.First(x => x.Error == null && x.Response?.Hash != null).Response.Hash;
                            var baseGas = blockHashResponses.First(x => x.Error == null && x.Response?.BaseFeePerGas != null).Response.BaseFeePerGas;
                            var gasUsed = blockHashResponses.First(x => x.Error == null && x.Response?.GasUsed != null).Response.GasUsed;
                            var burnedFee = (decimal)0;

                            if (extraPoolConfig?.ChainTypeOverride == "Ethereum")
                                burnedFee = (baseGas * gasUsed / EthereumConstants.Wei);

                            block.Hash = blockHash;
                            block.Status = BlockStatus.Confirmed;
                            block.ConfirmationProgress = 1;
                            block.BlockHeight = (ulong)blockInfo.Height;
                            block.Reward = GetBaseBlockReward(chainType, block.BlockHeight);
                            block.Type = "block";

                            if (extraConfig?.KeepUncles == false)
                                block.Reward += blockInfo.Uncles.Length * (block.Reward / 32);

                            if (extraConfig?.KeepTransactionFees == false && blockInfo.Transactions?.Length > 0)
                                block.Reward += await GetTxRewardAsync(blockInfo, ct) - burnedFee;

                            logger.Info(() => $"[{LogCategory}] Unlocked block {block.BlockHeight} worth {FormatAmount(block.Reward)}");

                            messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                        }

                        continue;
                    }

                    var heightMin = block.BlockHeight - BlockSearchOffset;
                    var heightMax = Math.Min(block.BlockHeight + BlockSearchOffset, latestBlockHeight);
                    var range = new List<long>();

                    for (var k = heightMin; k < heightMax; k++)
                        range.Add((long)k);

                    var blockInfo2s = await FetchBlocks(blockCache, ct, range.ToArray());

                    foreach (var blockInfo2 in blockInfo2s)
                    {
                        if (blockInfo2.Uncles.Length > 0)
                        {
                            var uncleBatch = blockInfo2.Uncles.Select((x, index) => new DaemonCmd(EC.GetUncleByBlockNumberAndIndex,
                                    new[] { blockInfo2.Height.Value.ToStringHexWithPrefix(), index.ToStringHexWithPrefix() }))
                                .ToArray();

                            logger.Info(() => $"[{LogCategory}] Fetching {blockInfo2.Uncles.Length} uncles for block {blockInfo2.Height}");

                            var uncleResponses = await daemon.ExecuteBatchAnyAsync(logger, ct, uncleBatch);

                            logger.Info(() => $"[{LogCategory}] Fetched {uncleResponses.Count(x => x.Error == null && x.Response != null)} uncles for block {blockInfo2.Height}");

                            var uncle = uncleResponses.Where(x => x.Error == null && x.Response != null)
                                .Select(x => x.Response.ToObject<DaemonResponses.Block>())
                                .FirstOrDefault(x => string.Equals(x.Miner, poolConfig.Address, StringComparison.OrdinalIgnoreCase));

                            if (uncle != null)
                            {
                                if (latestBlockHeight - uncle.Height.Value >= EthereumConstants.MinConfimations)
                                {
                                    var blockHashUncleResponses = await daemon.ExecuteCmdAllAsync<DaemonResponses.Block>(logger, EC.GetBlockByNumber, ct,
                                        new[] { (object)uncle.Height.Value.ToStringHexWithPrefix(), true });
                                    var blockHashUncle = blockHashUncleResponses.First(x => x.Error == null && x.Response?.Hash != null).Response.Hash;

                                    block.Hash = blockHashUncle;
                                    block.Status = BlockStatus.Confirmed;
                                    block.ConfirmationProgress = 1;
                                    block.Reward = GetUncleReward(chainType, uncle.Height.Value, blockInfo2.Height.Value);
                                    block.BlockHeight = uncle.Height.Value;
                                    block.Type = EthereumConstants.BlockTypeUncle;

                                    logger.Info(() => $"[{LogCategory}] Unlocked uncle for block {blockInfo2.Height.Value} at height {uncle.Height.Value} worth {FormatAmount(block.Reward)}");

                                    messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                                }

                                else
                                    logger.Info(() => $"[{LogCategory}] Got immature matching uncle for block {blockInfo2.Height.Value}. Will try again.");

                                break;
                            }
                        }
                    }

                    if (block.Status == BlockStatus.Pending && block.ConfirmationProgress > 0.75)
                    {
                        block.Hash = "0x0";
                        block.Status = BlockStatus.Orphaned;
                        block.Reward = 0;

                        messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                    }
                }
            }

            return result.ToArray();
        }

        public Task CalculateBlockEffortAsync(IMiningPool pool, Block block, double accumulatedBlockShareDiff, CancellationToken ct)
        {
            block.Effort = accumulatedBlockShareDiff / block.NetworkDifficulty;

            return Task.FromResult(true);
        }

        public override async Task<decimal> UpdateBlockRewardBalancesAsync(IDbConnection con, IDbTransaction tx, IMiningPool pool, Block block, CancellationToken ct)
        {
            var blockRewardRemaining = await base.UpdateBlockRewardBalancesAsync(con, tx, pool, block, ct);

            blockRewardRemaining -= EthereumConstants.StaticTransactionFeeReserve;

            return blockRewardRemaining;
        }

        public async Task PayoutAsync(IMiningPool pool, Balance[] balances, CancellationToken ct)
        {
            var infoResponse = await daemon.ExecuteCmdSingleAsync<string>(logger, EC.GetPeerCount, ct);

            if (networkType == EthereumNetworkType.Mainnet &&
                (infoResponse.Error != null || string.IsNullOrEmpty(infoResponse.Response) ||
                    infoResponse.Response.IntegralFromHex<int>() < EthereumConstants.MinPayoutPeerCount))
            {
                logger.Warn(() => $"[{LogCategory}] Payout aborted. Not enough peers (4 required)");
                return;
            }

            var txHashes = new List<string>();

            foreach (var balance in balances)
            {
                try
                {
                    var txHash = await PayoutAsync(balance, ct);
                    txHashes.Add(txHash);
                }

                catch (Exception ex)
                {
                    logger.Error(ex);

                    NotifyPayoutFailure(poolConfig.Id, new[] { balance }, ex.Message, null);
                }
            }

            if (txHashes.Any())
                NotifyPayoutSuccess(poolConfig.Id, balances, txHashes.ToArray(), null);
        }

        #endregion // IPayoutHandler

        private async Task<DaemonResponses.Block[]> FetchBlocks(Dictionary<long, DaemonResponses.Block> blockCache, CancellationToken ct, params long[] blockHeights)
        {
            var cacheMisses = blockHeights.Where(x => !blockCache.ContainsKey(x)).ToArray();

            if (cacheMisses.Any())
            {
                var blockBatch = cacheMisses.Select(height => new DaemonCmd(EC.GetBlockByNumber,
                    new[]
                    {
                        (object) height.ToStringHexWithPrefix(),
                        true
                    })).ToArray();

                var tmp = await daemon.ExecuteBatchAnyAsync(logger, ct, blockBatch);

                var transformed = tmp
                    .Where(x => x.Error == null && x.Response != null)
                    .Select(x => x.Response?.ToObject<DaemonResponses.Block>())
                    .Where(x => x != null)
                    .ToArray();

                foreach (var block in transformed)
                    blockCache[(long)block.Height.Value] = block;
            }

            return blockHeights.Select(x => blockCache[x]).ToArray();
        }

        internal static decimal GetBaseBlockReward(GethChainType chainType, ulong height)
        {
            switch (chainType)
            {
                case GethChainType.Ethereum:
                    if (height >= EthereumConstants.ConstantinopleHardForkHeight)
                        return EthereumConstants.ConstantinopleBlockReward;

                    if (height >= EthereumConstants.ByzantiumHardForkHeight)
                        return EthereumConstants.ByzantiumBlockReward;

                    return EthereumConstants.HomesteadBlockReward;

                case GethChainType.Callisto:
                    return CallistoConstants.BaseRewardInitial * (CallistoConstants.TreasuryPercent / 100);

                default:
                    throw new Exception("Unable to determine block reward: Unsupported chain type");
            }
        }

        private async Task<decimal> GetTxRewardAsync(DaemonResponses.Block blockInfo, CancellationToken ct)
        {
            var batch = blockInfo.Transactions.Select(tx => new DaemonCmd(EC.GetTxReceipt, new[] { tx.Hash }))
                .ToArray();

            var results = await daemon.ExecuteBatchAnyAsync(logger, ct, batch);

            if (results.Any(x => x.Error != null))
                throw new Exception($"Error fetching tx receipts: {string.Join(", ", results.Where(x => x.Error != null).Select(y => y.Error.Message))}");

            var gasUsed = results.Select(x => x.Response.ToObject<TransactionReceipt>())
                .ToDictionary(x => x.TransactionHash, x => x.GasUsed);

            var result = blockInfo.Transactions.Sum(x => (ulong)gasUsed[x.Hash] * ((decimal)x.GasPrice / EthereumConstants.Wei));

            return result;
        }

        internal static decimal GetUncleReward(GethChainType chainType, ulong uheight, ulong height)
        {
            var reward = GetBaseBlockReward(chainType, height);

            reward *= uheight + 8 - height;
            reward /= 8m;

            return reward;
        }

        private async Task DetectChainAsync(CancellationToken ct)
        {
            var commands = new[]
            {
                new DaemonCmd(EC.GetNetVersion),
            };

            var results = await daemon.ExecuteBatchAnyAsync(logger, ct, commands);

            if (results.Any(x => x.Error != null))
            {
                var errors = results.Take(1).Where(x => x.Error != null)
                    .ToArray();

                if (errors.Any())
                    throw new Exception($"Chain detection failed: {string.Join(", ", errors.Select(y => y.Error.Message))}");
            }

            var netVersion = results[0].Response.ToObject<string>();
            var gethChain = extraPoolConfig?.ChainTypeOverride ?? "Ethereum";

            EthereumUtils.DetectNetworkAndChain(netVersion, gethChain, out networkType, out chainType);
        }

        private async Task<string> PayoutAsync(Balance balance, CancellationToken ct)
        {
            logger.Info(() => $"[{LogCategory}] Sending {FormatAmount(balance.Amount)} to {balance.Address}");

            var amount = (BigInteger)Math.Floor(balance.Amount * EthereumConstants.Wei);

            var request = new SendTransactionRequest
            {
                From = poolConfig.Address,
                To = balance.Address,
                Value = amount.ToString("x").TrimStart('0'),
            };

            if (extraPoolConfig?.ChainTypeOverride == "Ethereum")
            {
                var maxPriorityFeePerGas = await daemon.ExecuteCmdSingleAsync<string>(logger, EC.MaxPriorityFeePerGas, ct);
                request.Gas = extraConfig.Gas;
                request.MaxPriorityFeePerGas = maxPriorityFeePerGas.Response.IntegralFromHex<ulong>();
                request.MaxFeePerGas = extraConfig.MaxFeePerGas;
            }

            var response = await daemon.ExecuteCmdSingleAsync<string>(logger, EC.SendTx, ct, new[] { request });

            if (response.Error != null)
                throw new Exception($"{EC.SendTx} returned error: {response.Error.Message} code {response.Error.Code}");

            if (string.IsNullOrEmpty(response.Response) || EthereumConstants.ZeroHashPattern.IsMatch(response.Response))
                throw new Exception($"{EC.SendTx} did not return a valid transaction hash");

            var txHash = response.Response;
            logger.Info(() => $"[{LogCategory}] Payment transaction id: {txHash}");

            await PersistPaymentsAsync(new[] { balance }, txHash);

            return txHash;
        }
    }
}