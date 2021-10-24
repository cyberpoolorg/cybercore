using Autofac;
using AutoMapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cybercore.Blockchain.Bitcoin.Configuration;
using Cybercore.Blockchain.Bitcoin.DaemonResponses;
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
using Newtonsoft.Json.Linq;
using Block = Cybercore.Persistence.Model.Block;
using Contract = Cybercore.Contracts.Contract;

namespace Cybercore.Blockchain.Bitcoin
{
    [CoinFamily(CoinFamily.Bitcoin)]
    public class BitcoinPayoutHandler : PayoutHandlerBase,
        IPayoutHandler
    {
        public BitcoinPayoutHandler(
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

        protected readonly IComponentContext ctx;
        protected DaemonClient daemon;
        protected BitcoinDaemonEndpointConfigExtra extraPoolConfig;
        protected BitcoinPoolPaymentProcessingConfigExtra extraPoolPaymentProcessingConfig;

        protected override string LogCategory => "Bitcoin Payout Handler";

        #region IPayoutHandler

        public virtual Task ConfigureAsync(ClusterConfig clusterConfig, PoolConfig poolConfig, CancellationToken ct)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;

            extraPoolConfig = poolConfig.Extra.SafeExtensionDataAs<BitcoinDaemonEndpointConfigExtra>();
            extraPoolPaymentProcessingConfig = poolConfig.PaymentProcessing.Extra.SafeExtensionDataAs<BitcoinPoolPaymentProcessingConfigExtra>();

            logger = LogUtil.GetPoolScopedLogger(typeof(BitcoinPayoutHandler), poolConfig);

            var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();
            daemon = new DaemonClient(jsonSerializerSettings, messageBus, clusterConfig.ClusterName ?? poolConfig.PoolName, poolConfig.Id);
            daemon.Configure(poolConfig.Daemons);

            return Task.FromResult(true);
        }

        public virtual async Task<Block[]> ClassifyBlocksAsync(IMiningPool pool, Block[] blocks, CancellationToken ct)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(blocks, nameof(blocks));

            var coin = poolConfig.Template.As<CoinTemplate>();
            var pageSize = 100;
            var pageCount = (int)Math.Ceiling(blocks.Length / (double)pageSize);
            var result = new List<Block>();
            int minConfirmations;

            if (coin is BitcoinTemplate bitcoinTemplate)
                minConfirmations = extraPoolConfig?.MinimumConfirmations ?? bitcoinTemplate.CoinbaseMinConfimations ?? BitcoinConstants.CoinbaseMinConfimations;
            else
                minConfirmations = extraPoolConfig?.MinimumConfirmations ?? BitcoinConstants.CoinbaseMinConfimations;

            for (var i = 0; i < pageCount; i++)
            {
                var page = blocks
                    .Skip(i * pageSize)
                    .Take(pageSize)
                    .ToArray();

                var batch = page.Select(block => new DaemonCmd(BitcoinCommands.GetTransaction,
                    new[] { block.TransactionConfirmationData })).ToArray();

                var results = await daemon.ExecuteBatchAnyAsync(logger, ct, batch);

                for (var j = 0; j < results.Length; j++)
                {
                    var cmdResult = results[j];

                    var transactionInfo = cmdResult.Response?.ToObject<Transaction>();
                    var block = page[j];

                    if (cmdResult.Error != null)
                    {
                        if (cmdResult.Error.Code == -5)
                        {
                            block.Status = BlockStatus.Orphaned;
                            block.Reward = 0;
                            result.Add(block);

                            logger.Info(() => $"[{LogCategory}] Block {block.BlockHeight} classified as orphaned due to daemon error {cmdResult.Error.Code}");

                            messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                        }

                        else
                            logger.Warn(() => $"[{LogCategory}] Daemon reports error '{cmdResult.Error.Message}' (Code {cmdResult.Error.Code}) for transaction {page[j].TransactionConfirmationData}");
                    }

                    else if (transactionInfo?.Details == null || transactionInfo.Details.Length == 0)
                    {
                        block.Status = BlockStatus.Orphaned;
                        block.Reward = 0;
                        result.Add(block);

                        logger.Info(() => $"[{LogCategory}] Block {block.BlockHeight} classified as orphaned due to missing tx details");
                    }

                    else
                    {
                        switch (transactionInfo.Details[0].Category)
                        {
                            case "immature":
                                block.ConfirmationProgress = Math.Min(1.0d, (double)transactionInfo.Confirmations / minConfirmations);
                                block.Reward = transactionInfo.Details[0].Amount;
                                result.Add(block);

                                messageBus.NotifyBlockConfirmationProgress(poolConfig.Id, block, coin);
                                break;

                            case "generate":
                                block.Status = BlockStatus.Confirmed;
                                block.ConfirmationProgress = 1;
                                block.Reward = transactionInfo.Details[0].Amount;
                                result.Add(block);

                                logger.Info(() => $"[{LogCategory}] Unlocked block {block.BlockHeight} worth {FormatAmount(block.Reward)}");

                                messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                                break;

                            default:
                                logger.Info(() => $"[{LogCategory}] Block {block.BlockHeight} classified as orphaned. Category: {transactionInfo.Details[0].Category}");

                                block.Status = BlockStatus.Orphaned;
                                block.Reward = 0;
                                result.Add(block);

                                messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                                break;
                        }
                    }
                }
            }

            return result.ToArray();
        }

        public virtual Task CalculateBlockEffortAsync(IMiningPool pool, Block block, double accumulatedBlockShareDiff, CancellationToken ct)
        {
            block.Effort = accumulatedBlockShareDiff / block.NetworkDifficulty;

            return Task.FromResult(true);
        }

        public virtual async Task PayoutAsync(IMiningPool pool, Balance[] balances, CancellationToken ct)
        {
            Contract.RequiresNonNull(balances, nameof(balances));

            var roundnum = poolConfig.Template.Symbol == "DVT" ? 3 : 4;

            var amounts = balances
                .Where(x => x.Amount > 0)
                .ToDictionary(x => x.Address, x => Math.Round(x.Amount, roundnum));

            if (amounts.Count == 0)
                return;

            logger.Info(() => $"[{LogCategory}] Paying {FormatAmount(balances.Sum(x => x.Amount))} to {balances.Length} addresses");

            object[] args;

            if (extraPoolPaymentProcessingConfig?.MinersPayTxFees == true)
            {
                var identifier = !string.IsNullOrEmpty(clusterConfig.PaymentProcessing?.CoinbaseString) ?
                    clusterConfig.PaymentProcessing.CoinbaseString.Trim() : "Cybercore";

                var comment = $"{identifier} Payment";
                var subtractFeesFrom = amounts.Keys.ToArray();

                if (!poolConfig.Template.As<BitcoinTemplate>().HasMasterNodes)
                {
                    args = new object[]
                    {
                        string.Empty,
                        amounts,
                        1,
                        comment,
                        subtractFeesFrom,
                    };
                }

                else
                {
                    args = new object[]
                    {
                        "",
                        amounts,
                        1,
                        false,
                        comment,
                        subtractFeesFrom,
                        false,
                        false,
                    };
                }
            }

            else
            {
                args = new object[]
                {
                    string.Empty,
                    amounts,
                };
            }

            var didUnlockWallet = false;

        tryTransfer:
            var result = await daemon.ExecuteCmdSingleAsync<string>(logger, BitcoinCommands.SendMany, ct, args, new JsonSerializerSettings());

            if (result.Error == null)
            {
                if (didUnlockWallet)
                {
                    logger.Info(() => $"[{LogCategory}] Locking wallet");
                    await daemon.ExecuteCmdSingleAsync<JToken>(logger, BitcoinCommands.WalletLock, ct);
                }

                var txId = result.Response;

                if (string.IsNullOrEmpty(txId))
                    logger.Error(() => $"[{LogCategory}] {BitcoinCommands.SendMany} did not return a transaction id!");
                else
                    logger.Info(() => $"[{LogCategory}] Payment transaction id: {txId}");

                await PersistPaymentsAsync(balances, txId);

                NotifyPayoutSuccess(poolConfig.Id, balances, new[] { txId }, null);
            }

            else
            {
                if (result.Error.Code == (int)BitcoinRPCErrorCode.RPC_WALLET_UNLOCK_NEEDED && !didUnlockWallet)
                {
                    if (!string.IsNullOrEmpty(extraPoolPaymentProcessingConfig?.WalletPassword))
                    {
                        logger.Info(() => $"[{LogCategory}] Unlocking wallet");

                        var unlockResult = await daemon.ExecuteCmdSingleAsync<JToken>(logger, BitcoinCommands.WalletPassphrase, ct, new[]
                        {
                            (object) extraPoolPaymentProcessingConfig.WalletPassword,
                            (object) 5
                        });

                        if (unlockResult.Error == null)
                        {
                            didUnlockWallet = true;
                            goto tryTransfer;
                        }

                        else
                            logger.Error(() => $"[{LogCategory}] {BitcoinCommands.WalletPassphrase} returned error: {result.Error.Message} code {result.Error.Code}");
                    }

                    else
                        logger.Error(() => $"[{LogCategory}] Wallet is locked but walletPassword was not configured. Unable to send funds.");
                }

                else
                {
                    logger.Error(() => $"[{LogCategory}] {BitcoinCommands.SendMany} returned error: {result.Error.Message} code {result.Error.Code}");

                    NotifyPayoutFailure(poolConfig.Id, balances, $"{BitcoinCommands.SendMany} returned error: {result.Error.Message} code {result.Error.Code}", null);
                }
            }
        }

        #endregion // IPayoutHandler
    }
}