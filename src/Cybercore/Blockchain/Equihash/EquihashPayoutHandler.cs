using Autofac;
using AutoMapper;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cybercore.Blockchain.Bitcoin;
using Cybercore.Blockchain.Bitcoin.DaemonResponses;
using Cybercore.Blockchain.Equihash.Configuration;
using Cybercore.Blockchain.Equihash.DaemonRequests;
using Cybercore.Blockchain.Equihash.DaemonResponses;
using Cybercore.Configuration;
using Cybercore.Extensions;
using Cybercore.Messaging;
using Cybercore.Mining;
using Cybercore.Persistence;
using Cybercore.Persistence.Model;
using Cybercore.Persistence.Repositories;
using Cybercore.Time;
using NBitcoin;
using Newtonsoft.Json.Linq;
using Contract = Cybercore.Contracts.Contract;
using IBlockRepository = Cybercore.Persistence.Repositories.IBlockRepository;

namespace Cybercore.Blockchain.Equihash
{
    [CoinFamily(CoinFamily.Equihash)]
    public class EquihashPayoutHandler : BitcoinPayoutHandler
    {
        public EquihashPayoutHandler(
            IComponentContext ctx,
            IConnectionFactory cf,
            IMapper mapper,
            IShareRepository shareRepo,
            IBlockRepository blockRepo,
            IBalanceRepository balanceRepo,
            IPaymentRepository paymentRepo,
            IMasterClock clock,
            IMessageBus messageBus) :
            base(ctx, cf, mapper, shareRepo, blockRepo, balanceRepo, paymentRepo, clock, messageBus)
        {
        }

        protected EquihashPoolConfigExtra poolExtraConfig;
        protected bool supportsNativeShielding;
        protected Network network;
        protected EquihashCoinTemplate.EquihashNetworkParams chainConfig;
        protected override string LogCategory => "Equihash Payout Handler";
        protected const decimal TransferFee = 0.0001m;
        protected const int ZMinConfirmations = 8;

        #region IPayoutHandler

        public override async Task ConfigureAsync(ClusterConfig clusterConfig, PoolConfig poolConfig, CancellationToken ct)
        {
            await base.ConfigureAsync(clusterConfig, poolConfig, ct);

            poolExtraConfig = poolConfig.Extra.SafeExtensionDataAs<EquihashPoolConfigExtra>();

            var blockchainInfoResponse = await daemon.ExecuteCmdSingleAsync<BlockchainInfo>(logger, BitcoinCommands.GetBlockchainInfo, ct);

            network = Network.GetNetwork(blockchainInfoResponse.Response.Chain.ToLower());

            chainConfig = poolConfig.Template.As<EquihashCoinTemplate>().GetNetwork(network.ChainName);

            var response = await daemon.ExecuteCmdSingleAsync<JObject>(logger, EquihashCommands.ZShieldCoinbase, ct);
            supportsNativeShielding = response.Error.Code != (int)BitcoinRPCErrorCode.RPC_METHOD_NOT_FOUND;
        }

        public override async Task PayoutAsync(IMiningPool pool, Balance[] balances, CancellationToken ct)
        {
            Contract.RequiresNonNull(balances, nameof(balances));

            if (supportsNativeShielding)
                await ShieldCoinbaseAsync(ct);
            else
                await ShieldCoinbaseEmulatedAsync(ct);

            var pageSize = 50;
            var pageCount = (int)Math.Ceiling(balances.Length / (double)pageSize);

            for (var i = 0; i < pageCount; i++)
            {
                var didUnlockWallet = false;

                var page = balances
                    .Skip(i * pageSize)
                    .Take(pageSize)
                    .ToArray();

                var amounts = page
                    .Where(x => x.Amount > 0)
                    .Select(x => new ZSendManyRecipient { Address = x.Address, Amount = Math.Round(x.Amount, 8) })
                    .ToList();

                if (amounts.Count == 0)
                    return;

                var pageAmount = amounts.Sum(x => x.Amount);

                var balanceResult = await daemon.ExecuteCmdSingleAsync<object>(logger, EquihashCommands.ZGetBalance, ct, new object[]
                {
                    poolExtraConfig.ZAddress,
                    ZMinConfirmations,
                });

                if (balanceResult.Error != null || (decimal)(double)balanceResult.Response - TransferFee < pageAmount)
                {
                    logger.Info(() => $"[{LogCategory}] Insufficient shielded balance for payment of {FormatAmount(pageAmount)}");
                    return;
                }

                logger.Info(() => $"[{LogCategory}] Paying {FormatAmount(pageAmount)} to {page.Length} addresses");

                var args = new object[]
                {
                    poolExtraConfig.ZAddress,
                    amounts,
                    ZMinConfirmations,
                    TransferFee
                };

            tryTransfer:
                var result = await daemon.ExecuteCmdSingleAsync<string>(logger, EquihashCommands.ZSendMany, ct, args);

                if (result.Error == null)
                {
                    var operationId = result.Response;

                    if (string.IsNullOrEmpty(operationId))
                        logger.Error(() => $"[{LogCategory}] {EquihashCommands.ZSendMany} did not return a operation id!");
                    else
                    {
                        logger.Info(() => $"[{LogCategory}] Tracking payment operation id: {operationId}");

                        var continueWaiting = true;

                        while (continueWaiting)
                        {
                            var operationResultResponse = await daemon.ExecuteCmdSingleAsync<ZCashAsyncOperationStatus[]>(logger,
                                EquihashCommands.ZGetOperationResult, ct, new object[] { new object[] { operationId } });

                            if (operationResultResponse.Error == null &&
                                operationResultResponse.Response?.Any(x => x.OperationId == operationId) == true)
                            {
                                var operationResult = operationResultResponse.Response.First(x => x.OperationId == operationId);

                                if (!Enum.TryParse(operationResult.Status, true, out ZOperationStatus status))
                                {
                                    logger.Error(() => $"Unrecognized operation status: {operationResult.Status}");
                                    break;
                                }

                                switch (status)
                                {
                                    case ZOperationStatus.Success:
                                        var txId = operationResult.Result?.Value<string>("txid") ?? string.Empty;
                                        logger.Info(() => $"[{LogCategory}] {EquihashCommands.ZSendMany} completed with transaction id: {txId}");

                                        await PersistPaymentsAsync(page, txId);
                                        NotifyPayoutSuccess(poolConfig.Id, page, new[] { txId }, null);

                                        continueWaiting = false;
                                        continue;

                                    case ZOperationStatus.Cancelled:
                                    case ZOperationStatus.Failed:
                                        logger.Error(() => $"{EquihashCommands.ZSendMany} failed: {operationResult.Error.Message} code {operationResult.Error.Code}");
                                        NotifyPayoutFailure(poolConfig.Id, page, $"{EquihashCommands.ZSendMany} failed: {operationResult.Error.Message} code {operationResult.Error.Code}", null);

                                        continueWaiting = false;
                                        continue;
                                }
                            }

                            logger.Info(() => $"[{LogCategory}] Waiting for completion: {operationId}");
                            await Task.Delay(TimeSpan.FromSeconds(10));
                        }
                    }
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
                            {
                                logger.Error(() => $"[{LogCategory}] {BitcoinCommands.WalletPassphrase} returned error: {result.Error.Message} code {result.Error.Code}");
                                NotifyPayoutFailure(poolConfig.Id, page, $"{BitcoinCommands.WalletPassphrase} returned error: {result.Error.Message} code {result.Error.Code}", null);
                                break;
                            }
                        }

                        else
                        {
                            logger.Error(() => $"[{LogCategory}] Wallet is locked but walletPassword was not configured. Unable to send funds.");
                            NotifyPayoutFailure(poolConfig.Id, page, "Wallet is locked but walletPassword was not configured. Unable to send funds.", null);
                            break;
                        }
                    }

                    else
                    {
                        logger.Error(() => $"[{LogCategory}] {EquihashCommands.ZSendMany} returned error: {result.Error.Message} code {result.Error.Code}");

                        NotifyPayoutFailure(poolConfig.Id, page, $"{EquihashCommands.ZSendMany} returned error: {result.Error.Message} code {result.Error.Code}", null);
                    }
                }
            }

            logger.Info(() => $"[{LogCategory}] Locking wallet");
            await daemon.ExecuteCmdSingleAsync<JToken>(logger, BitcoinCommands.WalletLock, ct);
        }

        #endregion // IPayoutHandler

        private async Task ShieldCoinbaseAsync(CancellationToken ct)
        {
            logger.Info(() => $"[{LogCategory}] Shielding ZCash Coinbase funds");

            var args = new object[]
            {
                poolConfig.Address,
                poolExtraConfig.ZAddress,
            };

            var result = await daemon.ExecuteCmdSingleAsync<ZCashShieldingResponse>(logger, EquihashCommands.ZShieldCoinbase, ct, args);

            if (result.Error != null)
            {
                if (result.Error.Code == -6)
                    logger.Info(() => $"[{LogCategory}] No funds to shield");
                else
                    logger.Error(() => $"[{LogCategory}] {EquihashCommands.ZShieldCoinbase} returned error: {result.Error.Message} code {result.Error.Code}");

                return;
            }

            var operationId = result.Response.OperationId;

            logger.Info(() => $"[{LogCategory}] {EquihashCommands.ZShieldCoinbase} operation id: {operationId}");

            var continueWaiting = true;

            while (continueWaiting)
            {
                var operationResultResponse = await daemon.ExecuteCmdSingleAsync<ZCashAsyncOperationStatus[]>(logger,
                    EquihashCommands.ZGetOperationResult, ct, new object[] { new object[] { operationId } });

                if (operationResultResponse.Error == null &&
                    operationResultResponse.Response?.Any(x => x.OperationId == operationId) == true)
                {
                    var operationResult = operationResultResponse.Response.First(x => x.OperationId == operationId);

                    if (!Enum.TryParse(operationResult.Status, true, out ZOperationStatus status))
                    {
                        logger.Error(() => $"Unrecognized operation status: {operationResult.Status}");
                        break;
                    }

                    switch (status)
                    {
                        case ZOperationStatus.Success:
                            logger.Info(() => $"[{LogCategory}] {EquihashCommands.ZShieldCoinbase} successful");

                            continueWaiting = false;
                            continue;

                        case ZOperationStatus.Cancelled:
                        case ZOperationStatus.Failed:
                            logger.Error(() => $"{EquihashCommands.ZShieldCoinbase} failed: {operationResult.Error.Message} code {operationResult.Error.Code}");

                            continueWaiting = false;
                            continue;
                    }
                }

                logger.Info(() => $"[{LogCategory}] Waiting for shielding operation completion: {operationId}");
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
        }

        private async Task ShieldCoinbaseEmulatedAsync(CancellationToken ct)
        {
            logger.Info(() => $"[{LogCategory}] Shielding ZCash Coinbase funds (emulated)");

            var unspentResult = await daemon.ExecuteCmdSingleAsync<Utxo[]>(logger, BitcoinCommands.ListUnspent, ct);

            if (unspentResult.Error != null)
            {
                logger.Error(() => $"[{LogCategory}] {BitcoinCommands.ListUnspent} returned error: {unspentResult.Error.Message} code {unspentResult.Error.Code}");
                return;
            }

            var balance = unspentResult.Response
                .Where(x => x.Spendable && x.Address == poolConfig.Address)
                .Sum(x => x.Amount);

            if (balance - TransferFee <= TransferFee)
            {
                logger.Info(() => $"[{LogCategory}] Balance {FormatAmount(balance)} too small for emulated shielding");
                return;
            }

            logger.Info(() => $"[{LogCategory}] Transferring {FormatAmount(balance - TransferFee)} to pool's z-addr");

            var recipient = new ZSendManyRecipient
            {
                Address = poolExtraConfig.ZAddress,
                Amount = balance - TransferFee
            };

            var args = new object[]
            {
                poolConfig.Address,
                new object[]
                {
                    recipient
                },
                1,
                TransferFee
            };

            var sendResult = await daemon.ExecuteCmdSingleAsync<string>(logger, EquihashCommands.ZSendMany, ct, args);

            if (sendResult.Error != null)
            {
                logger.Error(() => $"[{LogCategory}] {EquihashCommands.ZSendMany} returned error: {unspentResult.Error.Message} code {unspentResult.Error.Code}");
                return;
            }

            var operationId = sendResult.Response;

            logger.Info(() => $"[{LogCategory}] {EquihashCommands.ZSendMany} operation id: {operationId}");

            var continueWaiting = true;

            while (continueWaiting)
            {
                var operationResultResponse = await daemon.ExecuteCmdSingleAsync<ZCashAsyncOperationStatus[]>(logger,
                    EquihashCommands.ZGetOperationResult, ct, new object[] { new object[] { operationId } });

                if (operationResultResponse.Error == null &&
                    operationResultResponse.Response?.Any(x => x.OperationId == operationId) == true)
                {
                    var operationResult = operationResultResponse.Response.First(x => x.OperationId == operationId);

                    if (!Enum.TryParse(operationResult.Status, true, out ZOperationStatus status))
                    {
                        logger.Error(() => $"Unrecognized operation status: {operationResult.Status}");
                        break;
                    }

                    switch (status)
                    {
                        case ZOperationStatus.Success:
                            var txId = operationResult.Result?.Value<string>("txid") ?? string.Empty;
                            logger.Info(() => $"[{LogCategory}] Transfer completed with transaction id: {txId}");

                            continueWaiting = false;
                            continue;

                        case ZOperationStatus.Cancelled:
                        case ZOperationStatus.Failed:
                            logger.Error(() => $"{EquihashCommands.ZSendMany} failed: {operationResult.Error.Message} code {operationResult.Error.Code}");

                            continueWaiting = false;
                            continue;
                    }
                }

                logger.Info(() => $"[{LogCategory}] Waiting for shielding transfer completion: {operationId}");
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
        }
    }
}