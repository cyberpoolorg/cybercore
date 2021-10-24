using Autofac;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cybercore.Blockchain.Bitcoin.Configuration;
using Cybercore.Blockchain.Bitcoin.DaemonResponses;
using Cybercore.Configuration;
using Cybercore.Contracts;
using Cybercore.DaemonInterface;
using Cybercore.Extensions;
using Cybercore.JsonRpc;
using Cybercore.Messaging;
using Cybercore.Notifications.Messages;
using Cybercore.Time;
using Cybercore.Util;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Cybercore.Util.ActionUtils;

namespace Cybercore.Blockchain.Bitcoin
{
    public abstract class BitcoinJobManagerBase<TJob> : JobManagerBase<TJob>
    {
        protected BitcoinJobManagerBase(
            IComponentContext ctx,
            IMasterClock clock,
            IMessageBus messageBus,
            IExtraNonceProvider extraNonceProvider) :
            base(ctx, messageBus)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));
            Contract.RequiresNonNull(extraNonceProvider, nameof(extraNonceProvider));

            this.clock = clock;
            this.extraNonceProvider = extraNonceProvider;
        }

        protected readonly IMasterClock clock;
        protected DaemonClient daemon;
        protected readonly IExtraNonceProvider extraNonceProvider;
        protected const int ExtranonceBytes = 4;
        protected int maxActiveJobs = 4;
        protected bool hasLegacyDaemon;
        protected BitcoinPoolConfigExtra extraPoolConfig;
        protected BitcoinPoolPaymentProcessingConfigExtra extraPoolPaymentProcessingConfig;
        protected readonly List<TJob> validJobs = new();
        protected DateTime? lastJobRebroadcast;
        protected bool hasSubmitBlockMethod;
        protected bool isPoS;
        protected bool newDifficulty;
        protected TimeSpan jobRebroadcastTimeout;
        protected Network network;
        protected IDestination poolAddressDestination;

        protected virtual object[] GetBlockTemplateParams()
        {
            return new object[]
            {
                new
                {
                    rules = new[] {"segwit"},
                }
            };
        }

        protected virtual void SetupJobUpdates(CancellationToken ct)
        {
            jobRebroadcastTimeout = TimeSpan.FromSeconds(Math.Max(1, poolConfig.JobRebroadcastTimeout));
            var blockFound = blockFoundSubject.Synchronize();
            var pollTimerRestart = blockFoundSubject.Synchronize();

            var triggers = new List<IObservable<(bool Force, string Via, string Data)>>
            {
                blockFound.Select(x => (false, JobRefreshBy.BlockFound, (string) null))
            };

            if (extraPoolConfig?.BtStream == null)
            {
                var zmq = poolConfig.Daemons
                    .Where(x => !string.IsNullOrEmpty(x.Extra.SafeExtensionDataAs<BitcoinDaemonEndpointConfigExtra>()?.ZmqBlockNotifySocket))
                    .ToDictionary(x => x, x =>
                    {
                        var extra = x.Extra.SafeExtensionDataAs<BitcoinDaemonEndpointConfigExtra>();
                        var topic = !string.IsNullOrEmpty(extra.ZmqBlockNotifyTopic?.Trim()) ? extra.ZmqBlockNotifyTopic.Trim() : BitcoinConstants.ZmqPublisherTopicBlockHash;

                        return (Socket: extra.ZmqBlockNotifySocket, Topic: topic);
                    });

                if (zmq.Count > 0)
                {
                    logger.Info(() => $"Subscribing to ZMQ push-updates from {string.Join(", ", zmq.Values)}");

                    var blockNotify = daemon.ZmqSubscribe(logger, ct, zmq)
                        .Select(msg =>
                        {
                            using (msg)
                            {
                                var result = msg[1].Read().ToHexString();
                                return result;
                            }
                        })
                        .DistinctUntilChanged()
                        .Select(_ => (false, JobRefreshBy.PubSub, (string)null))
                        .Publish()
                        .RefCount();

                    pollTimerRestart = Observable.Merge(
                         blockFound,
                         blockNotify.Select(_ => Unit.Default))
                        .Publish()
                        .RefCount();

                    triggers.Add(blockNotify);
                }

                if (poolConfig.BlockRefreshInterval > 0)
                {
                    var pollingInterval = poolConfig.BlockRefreshInterval > 0 ? poolConfig.BlockRefreshInterval : 1000;

                    triggers.Add(Observable.Timer(TimeSpan.FromMilliseconds(pollingInterval))
                        .TakeUntil(pollTimerRestart)
                        .Select(_ => (false, JobRefreshBy.Poll, (string)null))
                        .Repeat());
                }

                else
                {
                    triggers.Add(Observable.Interval(TimeSpan.FromMilliseconds(1000))
                        .Select(_ => (false, JobRefreshBy.Initial, (string)null))
                        .TakeWhile(_ => !hasInitialBlockTemplate));
                }

                if (poolConfig.JobRebroadcastTimeout > 0)
                {
                    triggers.Add(Observable.Timer(jobRebroadcastTimeout)
                        .TakeUntil(pollTimerRestart)
                        .Select(_ => (true, JobRefreshBy.PollRefresh, (string)null))
                        .Repeat());
                }
            }

            else
            {
                var btStream = BtStreamSubscribe(extraPoolConfig.BtStream);

                if (poolConfig.JobRebroadcastTimeout > 0)
                {
                    var interval = TimeSpan.FromSeconds(Math.Max(1, poolConfig.JobRebroadcastTimeout - 0.1d));

                    triggers.Add(btStream
                        .Select(json =>
                        {
                            var force = !lastJobRebroadcast.HasValue || (clock.Now - lastJobRebroadcast >= interval);
                            return (force, !force ? JobRefreshBy.BlockTemplateStream : JobRefreshBy.BlockTemplateStreamRefresh, json);
                        })
                        .Publish()
                        .RefCount());
                }

                else
                {
                    triggers.Add(btStream
                        .Select(json => (false, JobRefreshBy.BlockTemplateStream, json))
                        .Publish()
                        .RefCount());
                }

                triggers.Add(Observable.Interval(TimeSpan.FromMilliseconds(1000))
                    .Select(_ => (false, JobRefreshBy.Initial, (string)null))
                    .TakeWhile(_ => !hasInitialBlockTemplate));
            }

            Jobs = Observable.Merge(triggers)
                .Select(x => Observable.FromAsync(() => UpdateJob(ct, x.Force, x.Via, x.Data)))
                .Concat()
                .Where(x => x.IsNew || x.Force)
                .Do(x =>
                {
                    if (x.IsNew)
                        hasInitialBlockTemplate = true;
                })
                .Select(x => GetJobParamsForStratum(x.IsNew))
                .Publish()
                .RefCount();
        }

        protected virtual async Task ShowDaemonSyncProgressAsync(CancellationToken ct)
        {
            if (hasLegacyDaemon)
            {
                await ShowDaemonSyncProgressLegacyAsync(ct);
                return;
            }

            var infos = await daemon.ExecuteCmdAllAsync<BlockchainInfo>(logger, BitcoinCommands.GetBlockchainInfo, ct);

            if (infos.Length > 0)
            {
                var blockCount = infos
                    .Max(x => x.Response?.Blocks);

                if (blockCount.HasValue)
                {
                    var peerInfo = await daemon.ExecuteCmdAnyAsync<PeerInfo[]>(logger, BitcoinCommands.GetPeerInfo, ct);
                    var peers = peerInfo.Response;

                    if (peers != null && peers.Length > 0)
                    {
                        var totalBlocks = peers.Max(x => x.StartingHeight);
                        var percent = totalBlocks > 0 ? (double)blockCount / totalBlocks * 100 : 0;
                        logger.Info(() => $"Daemons have downloaded {percent:0.00}% of blockchain from {peers.Length} peers");
                    }
                }
            }
        }

        private async Task UpdateNetworkStatsAsync(CancellationToken ct)
        {
            logger.LogInvoke();

            try
            {
                var results = await daemon.ExecuteBatchAnyAsync(logger, ct,
                    new DaemonCmd(BitcoinCommands.GetMiningInfo),
                    new DaemonCmd(BitcoinCommands.GetNetworkInfo),
                    new DaemonCmd(BitcoinCommands.GetNetworkHashPS)
                );

                if (results.Any(x => x.Error != null))
                {
                    var errors = results.Where(x => x.Error != null).ToArray();

                    if (errors.Any())
                        logger.Warn(() => $"Error(s) refreshing network stats: {string.Join(", ", errors.Select(y => y.Error.Message))}");
                }

                var miningInfoResponse = results[0].Response.ToObject<MiningInfo>();
                var networkInfoResponse = results[1].Response.ToObject<NetworkInfo>();

                var latestBlockHeight = miningInfoResponse.Blocks;
                var sampleSize = 300;
                var sampleBlockNumber = (latestBlockHeight - sampleSize);

                var blocksHash = await daemon.ExecuteBatchAnyAsync(logger, ct,
                    new DaemonCmd(BitcoinCommands.GetBlockHash, new[] { latestBlockHeight }),
                    new DaemonCmd(BitcoinCommands.GetBlockHash, new[] { sampleBlockNumber })
                );

                if (blocksHash.Any(x => x.Error != null))
                {
                    var errors = blocksHash.Where(x => x.Error != null).ToArray();

                    if (errors.Any())
                        logger.Warn(() => $"Error(s) get blockhash: {string.Join(", ", errors.Select(y => y.Error.Message))}");
                }

                var blockHash1 = blocksHash[0].Response;
                var blockHash2 = blocksHash[1].Response;

                var blocksInfo = await daemon.ExecuteBatchAnyAsync(logger, ct,
                    new DaemonCmd(BitcoinCommands.GetBlock, new[] { blockHash1 }),
                    new DaemonCmd(BitcoinCommands.GetBlock, new[] { blockHash2 })
                );

                if (blocksInfo.Any(x => x.Error != null))
                {
                    var errors = blocksInfo.Where(x => x.Error != null).ToArray();

                    if (errors.Any())
                        logger.Warn(() => $"Error(s) get blockinfo: {string.Join(", ", errors.Select(y => y.Error.Message))}");
                }

                var blockInfo1 = blocksInfo[0].Response.ToObject<BlockInfo1>();
                var blockInfo2 = blocksInfo[1].Response.ToObject<BlockInfo2>();

                var latestBlockTime = blockInfo1.Time;
                var sampleBlockTime = blockInfo2.Time;

                var blockTime = (double)(latestBlockTime - sampleBlockTime) / sampleSize;

                BlockchainStats.NetworkHashrate = miningInfoResponse.NetworkHashps;
                BlockchainStats.ConnectedPeers = networkInfoResponse.Connections;
                BlockchainStats.BlockTime = blockTime;

                if (BlockchainStats.NetworkHashrate == 0 && results[2].Error == null)
                    BlockchainStats.NetworkHashrate = results[2].Response.Value<double>();
            }

            catch (Exception e)
            {
                logger.Error(e);
            }
        }

        protected virtual async Task<(bool Accepted, string CoinbaseTx)> SubmitBlockAsync(Share share, string blockHex)
        {
            var results = await daemon.ExecuteBatchAnyAsync(logger, CancellationToken.None,
                hasSubmitBlockMethod
                    ? new DaemonCmd(BitcoinCommands.SubmitBlock, new[] { blockHex })
                    : new DaemonCmd(BitcoinCommands.GetBlockTemplate, new { mode = "submit", data = blockHex }),
                new DaemonCmd(BitcoinCommands.GetBlock, new[] { share.BlockHash }));

            var submitResult = results[0];
            var submitError = submitResult.Error?.Message ??
                submitResult.Error?.Code.ToString(CultureInfo.InvariantCulture) ??
                submitResult.Response?.ToString();

            if (!string.IsNullOrEmpty(submitError))
            {
                logger.Warn(() => $"Block {share.BlockHeight} submission failed with: {submitError}");
                messageBus.SendMessage(new AdminNotification("Block submission failed", $"Pool {poolConfig.Id} {(!string.IsNullOrEmpty(share.Source) ? $"[{share.Source.ToUpper()}] " : string.Empty)}failed to submit block {share.BlockHeight}: {submitError}"));
                return (false, null);
            }

            var acceptResult = results[1];
            var block = acceptResult.Response?.ToObject<DaemonResponses.Block>();
            var accepted = acceptResult.Error == null && block?.Hash == share.BlockHash;

            if (!accepted)
            {
                logger.Warn(() => $"Block {share.BlockHeight} submission failed for pool {poolConfig.Id} because block was not found after submission");
                messageBus.SendMessage(new AdminNotification($"[{share.PoolId.ToUpper()}]-[{share.Source}] Block submission failed", $"[{share.PoolId.ToUpper()}]-[{share.Source}] Block {share.BlockHeight} submission failed for pool {poolConfig.Id} because block was not found after submission"));
            }

            return (accepted, block?.Transactions.FirstOrDefault());
        }

        protected virtual async Task<bool> AreDaemonsHealthyLegacyAsync(CancellationToken ct)
        {
            var responses = await daemon.ExecuteCmdAllAsync<DaemonInfo>(logger, BitcoinCommands.GetInfo, ct);

            if (responses.Where(x => x.Error?.InnerException?.GetType() == typeof(DaemonClientException))
                .Select(x => (DaemonClientException)x.Error.InnerException)
                .Any(x => x.Code == HttpStatusCode.Unauthorized))
                logger.ThrowLogPoolStartupException("Daemon reports invalid credentials");

            return responses.All(x => x.Error == null);
        }

        protected virtual async Task<bool> AreDaemonsConnectedLegacyAsync(CancellationToken ct)
        {
            var response = await daemon.ExecuteCmdAnyAsync<DaemonInfo>(logger, BitcoinCommands.GetInfo, ct);

            return response.Error == null && response.Response.Connections > 0;
        }

        protected virtual async Task ShowDaemonSyncProgressLegacyAsync(CancellationToken ct)
        {
            var infos = await daemon.ExecuteCmdAllAsync<DaemonInfo>(logger, BitcoinCommands.GetInfo, ct);

            if (infos.Length > 0)
            {
                var blockCount = infos
                    .Max(x => x.Response?.Blocks);

                if (blockCount.HasValue)
                {
                    var peerInfo = await daemon.ExecuteCmdAnyAsync<PeerInfo[]>(logger, BitcoinCommands.GetPeerInfo, ct);
                    var peers = peerInfo.Response;

                    if (peers != null && peers.Length > 0)
                    {
                        var totalBlocks = peers.Max(x => x.StartingHeight);
                        var percent = totalBlocks > 0 ? (double)blockCount / totalBlocks * 100 : 0;
                        logger.Info(() => $"Daemons have downloaded {percent:0.00}% of blockchain from {peers.Length} peers");
                    }
                }
            }
        }

        private async Task UpdateNetworkStatsLegacyAsync(CancellationToken ct)
        {
            logger.LogInvoke();

            try
            {
                var results = await daemon.ExecuteBatchAnyAsync(logger, ct,
                    new DaemonCmd(BitcoinCommands.GetConnectionCount),
                    new DaemonCmd(BitcoinCommands.GetMiningInfo)
                );

                if (results.Any(x => x.Error != null))
                {
                    var errors = results.Where(x => x.Error != null).ToArray();

                    if (errors.Any())
                        logger.Warn(() => $"Error(s) refreshing network stats: {string.Join(", ", errors.Select(y => y.Error.Message))}");
                }

                var connectionCountResponse = results[0].Response.ToObject<object>();
                var miningInfoResponse = results[1].Response.ToObject<MiningInfo>();

                var latestBlockHeight = miningInfoResponse.Blocks;
                var sampleSize = 300;
                var sampleBlockNumber = (latestBlockHeight - sampleSize);

                var blocksHash = await daemon.ExecuteBatchAnyAsync(logger, ct,
                    new DaemonCmd(BitcoinCommands.GetBlockHash, new[] { latestBlockHeight }),
                    new DaemonCmd(BitcoinCommands.GetBlockHash, new[] { sampleBlockNumber })
                );

                if (blocksHash.Any(x => x.Error != null))
                {
                    var errors = blocksHash.Where(x => x.Error != null).ToArray();

                    if (errors.Any())
                        logger.Warn(() => $"Error(s) get blockhash: {string.Join(", ", errors.Select(y => y.Error.Message))}");
                }

                var blockHash1 = blocksHash[0].Response;
                var blockHash2 = blocksHash[1].Response;

                var blocksInfo = await daemon.ExecuteBatchAnyAsync(logger, ct,
                    new DaemonCmd(BitcoinCommands.GetBlock, new[] { blockHash1 }),
                    new DaemonCmd(BitcoinCommands.GetBlock, new[] { blockHash2 })
                );

                if (blocksInfo.Any(x => x.Error != null))
                {
                    var errors = blocksInfo.Where(x => x.Error != null).ToArray();

                    if (errors.Any())
                        logger.Warn(() => $"Error(s) get blockinfo: {string.Join(", ", errors.Select(y => y.Error.Message))}");
                }

                var blockInfo1 = blocksInfo[0].Response.ToObject<BlockInfo1>();
                var blockInfo2 = blocksInfo[1].Response.ToObject<BlockInfo2>();

                var latestBlockTime = blockInfo1.Time;
                var sampleBlockTime = blockInfo2.Time;

                var blockTime = (double)(latestBlockTime - sampleBlockTime) / sampleSize;

                BlockchainStats.ConnectedPeers = (int)(long)connectionCountResponse;
                BlockchainStats.NetworkHashrate = miningInfoResponse.NetmHashps *= 1000000;
                BlockchainStats.BlockTime = blockTime;
            }

            catch (Exception e)
            {
                logger.Error(e);
            }
        }

        protected virtual void PostChainIdentifyConfigure()
        {
        }

        protected override void ConfigureDaemons()
        {
            var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

            daemon = new DaemonClient(jsonSerializerSettings, messageBus, clusterConfig.ClusterName ?? poolConfig.PoolName, poolConfig.Id);
            daemon.Configure(poolConfig.Daemons);
        }

        protected override async Task<bool> AreDaemonsHealthyAsync(CancellationToken ct)
        {
            if (hasLegacyDaemon)
                return await AreDaemonsHealthyLegacyAsync(ct);

            var responses = await daemon.ExecuteCmdAllAsync<BlockchainInfo>(logger, BitcoinCommands.GetBlockchainInfo, ct);

            if (responses.Where(x => x.Error?.InnerException?.GetType() == typeof(DaemonClientException))
                .Select(x => (DaemonClientException)x.Error.InnerException)
                .Any(x => x.Code == HttpStatusCode.Unauthorized))
                logger.ThrowLogPoolStartupException("Daemon reports invalid credentials");

            return responses.All(x => x.Error == null);
        }

        protected override async Task<bool> AreDaemonsConnectedAsync(CancellationToken ct)
        {
            if (hasLegacyDaemon)
                return await AreDaemonsConnectedLegacyAsync(ct);

            var response = await daemon.ExecuteCmdAnyAsync<NetworkInfo>(logger, BitcoinCommands.GetNetworkInfo, ct);

            return response.Error == null && response.Response?.Connections > 0;
        }

        protected override async Task EnsureDaemonsSynchedAsync(CancellationToken ct)
        {
            var syncPendingNotificationShown = false;

            while (true)
            {
                var response = await daemon.ExecuteCmdAllAsync<BlockTemplate>(logger,
                    BitcoinCommands.GetBlockTemplate, ct, GetBlockTemplateParams());

                var isSynched = response.All(x => x.Error == null);

                if (isSynched)
                {
                    logger.Info(() => "All daemons synched with blockchain");
                    break;
                }

                if (!syncPendingNotificationShown)
                {
                    logger.Info(() => "Daemons still syncing with network. Manager will be started once synced");
                    syncPendingNotificationShown = true;
                }

                await ShowDaemonSyncProgressAsync(ct);

                await Task.Delay(5000, ct);
            }
        }

        protected override async Task PostStartInitAsync(CancellationToken ct)
        {
            var commands = new[]
            {
                new DaemonCmd(BitcoinCommands.ValidateAddress, new[] { poolConfig.Address }),
                new DaemonCmd(BitcoinCommands.SubmitBlock),
                new DaemonCmd(!hasLegacyDaemon ? BitcoinCommands.GetBlockchainInfo : BitcoinCommands.GetInfo),
                new DaemonCmd(BitcoinCommands.GetDifficulty),
                new DaemonCmd(BitcoinCommands.GetAddressInfo, new[] { poolConfig.Address }),
            };

            var results = await daemon.ExecuteBatchAnyAsync(logger, ct, commands);

            if (results.Any(x => x.Error != null))
            {
                var resultList = results.ToList();

                var errors = results
                    .Where(x => x.Error != null && commands[resultList.IndexOf(x)].Method != BitcoinCommands.SubmitBlock)
                    .Where(x => x.Error != null && commands[resultList.IndexOf(x)].Method != BitcoinCommands.GetAddressInfo)
                    .ToArray();

                if (errors.Any())
                    logger.ThrowLogPoolStartupException($"Init RPC failed: {string.Join(", ", errors.Select(y => y.Error.Message))}");
            }

            var validateAddressResponse = results[0].Error == null ? results[0].Response.ToObject<ValidateAddressResponse>() : null;
            var submitBlockResponse = results[1];
            var blockchainInfoResponse = !hasLegacyDaemon ? results[2].Response.ToObject<BlockchainInfo>() : null;
            var daemonInfoResponse = hasLegacyDaemon ? results[2].Response.ToObject<DaemonInfo>() : null;
            var difficultyResponse = results[3].Response.ToObject<JToken>();
            var addressInfoResponse = results[4].Error == null ? results[4].Response.ToObject<AddressInfo>() : null;

            if (!hasLegacyDaemon)
                network = Network.GetNetwork(blockchainInfoResponse.Chain.ToLower());
            else
                network = daemonInfoResponse.Testnet ? Network.TestNet : Network.Main;

            PostChainIdentifyConfigure();

            if (validateAddressResponse is not { IsValid: true })
                logger.ThrowLogPoolStartupException($"Daemon reports pool-address '{poolConfig.Address}' as invalid");

            newDifficulty = difficultyResponse.Values().Any(x => x.Path == "proof-of-work");

            isPoS = poolConfig.Template is BitcoinTemplate { IsPseudoPoS: true } || difficultyResponse.Values().Any(x => x.Path == "proof-of-stake");

            if (!isPoS || !poolConfig.UseP2PK)
            {
                poolAddressDestination = AddressToDestination(poolConfig.Address, extraPoolConfig?.AddressType);
            }

            else
            {
                poolAddressDestination = new PubKey(poolConfig.PubKey ?? validateAddressResponse.PubKey);
            }

            if (clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
            {
                ConfigureRewards();
            }

            BlockchainStats.NetworkType = network.Name;
            BlockchainStats.RewardType = isPoS ? "POW / POS" : "POW";

            if (submitBlockResponse.Error?.Message?.ToLower() == "method not found")
                hasSubmitBlockMethod = false;
            else if (submitBlockResponse.Error?.Code == -1)
                hasSubmitBlockMethod = true;
            else
                logger.ThrowLogPoolStartupException("Unable detect block submission RPC method");

            if (!hasLegacyDaemon)
                await UpdateNetworkStatsAsync(ct);
            else
                await UpdateNetworkStatsLegacyAsync(ct);

            Observable.Interval(TimeSpan.FromMinutes(10))
                .Select(via => Observable.FromAsync(() =>
                    Guard(() => (!hasLegacyDaemon ? UpdateNetworkStatsAsync(ct) : UpdateNetworkStatsLegacyAsync(ct)),
                        ex => logger.Error(ex))))
                .Concat()
                .Subscribe();

            SetupCrypto();
            SetupJobUpdates(ct);
        }

        protected virtual IDestination AddressToDestination(string address, BitcoinAddressType? addressType)
        {
            if (!addressType.HasValue)
                return BitcoinUtils.AddressToDestination(address, network);

            switch (addressType.Value)
            {
                case BitcoinAddressType.BechSegwit:
                    return BitcoinUtils.BechSegwitAddressToDestination(poolConfig.Address, network, extraPoolConfig?.BechPrefix);

                case BitcoinAddressType.CashAddr:
                    return BitcoinUtils.CashAddrToDestination(poolConfig.Address, network);

                default:
                    return BitcoinUtils.AddressToDestination(poolConfig.Address, network);
            }
        }

        protected virtual void SetupCrypto()
        {

        }

        protected abstract Task<(bool IsNew, bool Force)> UpdateJob(CancellationToken ct, bool forceUpdate, string via = null, string json = null);
        protected abstract object GetJobParamsForStratum(bool isNew);

        protected void ConfigureRewards()
        {
            if (network.ChainName == ChainName.Mainnet &&
                DevDonation.Addresses.TryGetValue(poolConfig.Template.Symbol, out var address))
            {
                poolConfig.RewardRecipients = poolConfig.RewardRecipients.Concat(new[]
                {
                    new RewardRecipient
                    {
                        Address = address,
                        Percentage = DevDonation.Percent,
                        Type = "dev"
                    }
                }).ToArray();
            }
        }

        #region API-Surface

        public Network Network => network;
        public IObservable<object> Jobs { get; private set; }
        public BlockchainStats BlockchainStats { get; } = new();

        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            extraPoolConfig = poolConfig.Extra.SafeExtensionDataAs<BitcoinPoolConfigExtra>();
            extraPoolPaymentProcessingConfig = poolConfig.PaymentProcessing?.Extra?.SafeExtensionDataAs<BitcoinPoolPaymentProcessingConfigExtra>();

            if (extraPoolConfig?.MaxActiveJobs.HasValue == true)
                maxActiveJobs = extraPoolConfig.MaxActiveJobs.Value;

            hasLegacyDaemon = extraPoolConfig?.HasLegacyDaemon == true;

            base.Configure(poolConfig, clusterConfig);
        }

        public virtual async Task<bool> ValidateAddressAsync(string address, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(address))
                return false;

            var result = await daemon.ExecuteCmdAnyAsync<ValidateAddressResponse>(logger, ct, BitcoinCommands.ValidateAddress, new[] { address });

            return result.Response is { IsValid: true };
        }

        #endregion // API-Surface
    }
}