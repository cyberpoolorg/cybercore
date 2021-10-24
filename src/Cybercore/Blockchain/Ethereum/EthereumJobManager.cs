using Autofac;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cybercore.Blockchain.Bitcoin;
using Cybercore.Blockchain.Ethereum.Configuration;
using Cybercore.Blockchain.Ethereum.DaemonResponses;
using Cybercore.Configuration;
using Cybercore.Crypto.Hashing.Ethash;
using Cybercore.DaemonInterface;
using Cybercore.Extensions;
using Cybercore.JsonRpc;
using Cybercore.Messaging;
using Cybercore.Notifications.Messages;
using Cybercore.Stratum;
using Cybercore.Time;
using Cybercore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Block = Cybercore.Blockchain.Ethereum.DaemonResponses.Block;
using Contract = Cybercore.Contracts.Contract;
using EC = Cybercore.Blockchain.Ethereum.EthCommands;
using static Cybercore.Util.ActionUtils;

namespace Cybercore.Blockchain.Ethereum
{
    public class EthereumJobManager : JobManagerBase<EthereumJob>
    {
        public EthereumJobManager(
            IComponentContext ctx,
            IMasterClock clock,
            IMessageBus messageBus,
            IExtraNonceProvider extraNonceProvider,
            JsonSerializerSettings serializerSettings) :
            base(ctx, messageBus)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));
            Contract.RequiresNonNull(extraNonceProvider, nameof(extraNonceProvider));

            this.clock = clock;
            this.extraNonceProvider = extraNonceProvider;

            serializer = new JsonSerializer
            {
                ContractResolver = serializerSettings.ContractResolver
            };
        }

        private DaemonEndpointConfig[] daemonEndpoints;
        private DaemonClient daemon;
        private EthereumNetworkType networkType;
        private GethChainType chainType;
        private EthashFull ethash;
        private readonly IMasterClock clock;
        private readonly IExtraNonceProvider extraNonceProvider;
        private const int MaxBlockBacklog = 3;
        protected readonly Dictionary<string, EthereumJob> validJobs = new();
        private EthereumPoolConfigExtra extraPoolConfig;
        private readonly JsonSerializer serializer;

        protected async Task<bool> UpdateJobAsync(CancellationToken ct)
        {
            logger.LogInvoke();

            try
            {
                return UpdateJob(await GetBlockTemplateAsync(ct));
            }

            catch (Exception ex)
            {
                logger.Error(ex, () => $"Error during {nameof(UpdateJobAsync)}");
            }

            return false;
        }

        protected bool UpdateJob(EthereumBlockTemplate blockTemplate)
        {
            logger.LogInvoke();

            try
            {
                if (blockTemplate == null || blockTemplate.Header?.Length == 0)
                    return false;

                var job = currentJob;
                var isNew = currentJob == null ||
                    job.BlockTemplate.Height < blockTemplate.Height ||
                    job.BlockTemplate.Header != blockTemplate.Header;

                if (isNew)
                {
                    messageBus.NotifyChainHeight(poolConfig.Id, blockTemplate.Height, poolConfig.Template);

                    var jobId = NextJobId("x8");

                    job = new EthereumJob(jobId, blockTemplate, logger);

                    lock (jobLock)
                    {
                        validJobs[jobId] = job;

                        var obsoleteKeys = validJobs.Keys
                            .Where(key => validJobs[key].BlockTemplate.Height < job.BlockTemplate.Height - MaxBlockBacklog).ToArray();

                        foreach (var key in obsoleteKeys)
                            validJobs.Remove(key);
                    }

                    currentJob = job;

                    BlockchainStats.LastNetworkBlockTime = clock.Now;
                    BlockchainStats.BlockHeight = job.BlockTemplate.Height;
                    BlockchainStats.NetworkDifficulty = job.BlockTemplate.Difficulty;
                    BlockchainStats.NextNetworkTarget = job.BlockTemplate.Target;
                    BlockchainStats.NextNetworkBits = "";
                }

                return isNew;
            }

            catch (Exception ex)
            {
                logger.Error(ex, () => $"Error during {nameof(UpdateJob)}");
            }

            return false;
        }

        private async Task<EthereumBlockTemplate> GetBlockTemplateAsync(CancellationToken ct)
        {
            logger.LogInvoke();

            var commands = new[]
            {
                new DaemonCmd(EC.GetWork),
                new DaemonCmd(EC.GetBlockByNumber, new[] { (object) "latest", true })
            };

            var results = await daemon.ExecuteBatchAnyAsync(logger, ct, commands);

            if (results.Any(x => x.Error != null))
            {
                logger.Warn(() => $"Error(s) refreshing blocktemplate: {results.First(x => x.Error != null).Error.Message}");
                return null;
            }

            var work = results[0].Response.ToObject<string[]>();
            var block = results[1].Response.ToObject<Block>();

            if (work.Length < 4)
            {
                var currentHeight = block.Height.Value;
                work = work.Concat(new[] { (currentHeight + 1).ToStringHexWithPrefix() }).ToArray();
            }

            var height = work[3].IntegralFromHex<ulong>();
            var targetString = work[2];
            var target = BigInteger.Parse(targetString.Substring(2), NumberStyles.HexNumber);

            var result = new EthereumBlockTemplate
            {
                Header = work[0],
                Seed = work[1],
                Target = targetString,
                Difficulty = (ulong)BigInteger.Divide(EthereumConstants.BigMaxValue, target),
                Height = height
            };

            return result;
        }

        private async Task ShowDaemonSyncProgressAsync(CancellationToken ct)
        {
            var responses = await daemon.ExecuteCmdAllAsync<object>(logger, EC.GetSyncState, ct);
            var firstValidResponse = responses.FirstOrDefault(x => x.Error == null && x.Response != null)?.Response;

            if (firstValidResponse != null)
            {
                if (firstValidResponse is bool)
                    return;

                var syncStates = responses.Where(x => x.Error == null && x.Response != null && firstValidResponse is JObject)
                    .Select(x => ((JObject)x.Response).ToObject<SyncState>())
                    .ToArray();

                if (syncStates.Any())
                {
                    var response = await daemon.ExecuteCmdAllAsync<string>(logger, EC.GetPeerCount, ct);
                    var validResponses = response.Where(x => x.Error == null && x.Response != null).ToArray();
                    var peerCount = validResponses.Any() ? validResponses.Max(x => x.Response.IntegralFromHex<uint>()) : 0;

                    if (syncStates.Any(x => x.WarpChunksAmount != 0))
                    {
                        var warpChunkAmount = syncStates.Min(x => x.WarpChunksAmount);
                        var warpChunkProcessed = syncStates.Max(x => x.WarpChunksProcessed);
                        var percent = (double)warpChunkProcessed / warpChunkAmount * 100;

                        logger.Info(() => $"Daemons have downloaded {percent:0.00}% of warp-chunks from {peerCount} peers");
                    }

                    else if (syncStates.Any(x => x.HighestBlock != 0))
                    {
                        var lowestHeight = syncStates.Min(x => x.CurrentBlock);
                        var totalBlocks = syncStates.Max(x => x.HighestBlock);
                        var percent = (double)lowestHeight / totalBlocks * 100;

                        logger.Info(() => $"Daemons have downloaded {percent:0.00}% of blockchain from {peerCount} peers");
                    }
                }
            }
        }

        private async Task UpdateNetworkStatsAsync(CancellationToken ct)
        {
            logger.LogInvoke();

            try
            {
                var commands = new[]
                {
                    new DaemonCmd(EC.GetPeerCount),
                    new DaemonCmd(EC.GetBlockByNumber, new[] { (object) "latest", true })
                };

                var results = await daemon.ExecuteBatchAnyAsync(logger, ct, commands);

                if (results.Any(x => x.Error != null))
                {
                    var errors = results.Where(x => x.Error != null)
                        .ToArray();

                    if (errors.Any())
                        logger.Warn(() => $"Error(s) refreshing network stats: {string.Join(", ", errors.Select(y => y.Error.Message))})");
                }

                var peerCount = results[0].Response.ToObject<string>().IntegralFromHex<int>();
                var latestBlockInfo = results[1].Response.ToObject<Block>();

                var latestBlockHeight = latestBlockInfo.Height.Value;
                var latestBlockTimestamp = latestBlockInfo.Timestamp;
                var latestBlockDifficulty = latestBlockInfo.Difficulty.IntegralFromHex<ulong>();

                var sampleSize = (ulong)300;
                var sampleBlockNumber = latestBlockHeight - sampleSize;
                var sampleBlockResults = await daemon.ExecuteCmdAllAsync<Block>(logger, EC.GetBlockByNumber, ct, new[] { (object)sampleBlockNumber.ToStringHexWithPrefix(), true });
                var sampleBlockTimestamp = sampleBlockResults.First(x => x.Error == null && x.Response?.Height != null).Response.Timestamp;

                var blockTime = (double)(latestBlockTimestamp - sampleBlockTimestamp) / sampleSize;
                var networkHashrate = (double)(latestBlockDifficulty / blockTime);

                BlockchainStats.NetworkHashrate = blockTime > 0 ? networkHashrate : 0;
                BlockchainStats.ConnectedPeers = peerCount;
                BlockchainStats.BlockTime = blockTime;
            }

            catch (Exception e)
            {
                logger.Error(e);
            }
        }

        private async Task<bool> SubmitBlockAsync(Share share, string fullNonceHex, string headerHash, string mixHash)
        {
            var response = await daemon.ExecuteCmdAnyAsync<object>(logger, EC.SubmitWork, CancellationToken.None, new[]
            {
                fullNonceHex,
                headerHash,
                mixHash
            });

            if (response.Error != null || (bool?)response.Response == false)
            {
                var error = response.Error?.Message ?? response?.Response?.ToString();

                logger.Warn(() => $"Block {share.BlockHeight} submission failed with: {error}");
                messageBus.SendMessage(new AdminNotification("Block submission failed", $"Pool {poolConfig.Id} {(!string.IsNullOrEmpty(share.Source) ? $"[{share.Source.ToUpper()}] " : string.Empty)}failed to submit block {share.BlockHeight}: {error}"));

                return false;
            }

            return true;
        }

        private object[] GetJobParamsForStratum(bool isNew)
        {
            var job = currentJob;

            if (job != null)
            {
                return new object[]
                {
                    job.Id,
                    job.BlockTemplate.Seed.StripHexPrefix(),
                    job.BlockTemplate.Header.StripHexPrefix(),
                    isNew
                };
            }

            return new object[0];
        }

        private JsonRpcRequest DeserializeRequest(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            {
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    using (var jreader = new JsonTextReader(reader))
                    {
                        return serializer.Deserialize<JsonRpcRequest>(jreader);
                    }
                }
            }
        }

        #region API-Surface

        public IObservable<object> Jobs { get; private set; }

        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            extraPoolConfig = poolConfig.Extra.SafeExtensionDataAs<EthereumPoolConfigExtra>();

            daemonEndpoints = poolConfig.Daemons
                .Where(x => string.IsNullOrEmpty(x.Category))
                .ToArray();

            base.Configure(poolConfig, clusterConfig);

            if (poolConfig.EnableInternalStratum == true)
            {
                var dagDir = !string.IsNullOrEmpty(extraPoolConfig?.DagDir) ?
                    Environment.ExpandEnvironmentVariables(extraPoolConfig.DagDir) :
                    Dag.GetDefaultDagDirectory();

                Directory.CreateDirectory(dagDir);

                ethash = new EthashFull(3, dagDir);
            }
        }

        public bool ValidateAddress(string address)
        {
            if (string.IsNullOrEmpty(address))
                return false;

            if (EthereumConstants.ZeroHashPattern.IsMatch(address) ||
                !EthereumConstants.ValidAddressPattern.IsMatch(address))
                return false;

            return true;
        }

        public void PrepareWorker(StratumConnection client)
        {
            var context = client.ContextAs<EthereumWorkerContext>();
            context.ExtraNonce1 = extraNonceProvider.Next();
        }

        public async ValueTask<Share> SubmitShareAsync(StratumConnection worker, string[] request, CancellationToken ct)
        {
            Contract.RequiresNonNull(worker, nameof(worker));
            Contract.RequiresNonNull(request, nameof(request));

            logger.LogInvoke(new object[] { worker.ConnectionId });
            var context = worker.ContextAs<EthereumWorkerContext>();

            var jobId = request[1];
            var nonce = request[2];
            EthereumJob job;

            lock (jobLock)
            {
                if (!validJobs.TryGetValue(jobId, out job))
                    throw new StratumException(StratumError.MinusOne, "stale share");
            }

            var (share, fullNonceHex, headerHash, mixHash) = await job.ProcessShareAsync(worker, nonce, ethash, ct);

            share.PoolId = poolConfig.Id;
            share.NetworkDifficulty = BlockchainStats.NetworkDifficulty;
            share.Source = clusterConfig.ClusterName;
            share.Created = clock.Now;

            if (share.IsBlockCandidate)
            {
                logger.Info(() => $"Submitting block {share.BlockHeight}");

                share.IsBlockCandidate = await SubmitBlockAsync(share, fullNonceHex, headerHash, mixHash);

                if (share.IsBlockCandidate)
                {
                    logger.Info(() => $"Daemon accepted block {share.BlockHeight} submitted by {context.Miner}");
                }
            }

            return share;
        }

        public BlockchainStats BlockchainStats { get; } = new();

        #endregion // API-Surface

        #region Overrides

        protected override void ConfigureDaemons()
        {
            var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

            daemon = new DaemonClient(jsonSerializerSettings, messageBus, clusterConfig.ClusterName ?? poolConfig.PoolName, poolConfig.Id);
            daemon.Configure(daemonEndpoints);
        }

        protected override async Task<bool> AreDaemonsHealthyAsync(CancellationToken ct)
        {
            var responses = await daemon.ExecuteCmdAllAsync<Block>(logger, EC.GetBlockByNumber, ct, new[] { (object)"latest", true });

            if (responses.Where(x => x.Error?.InnerException?.GetType() == typeof(DaemonClientException))
                .Select(x => (DaemonClientException)x.Error.InnerException)
                .Any(x => x.Code == HttpStatusCode.Unauthorized))
                logger.ThrowLogPoolStartupException("Daemon reports invalid credentials");

            return responses.All(x => x.Error == null);
        }

        protected override async Task<bool> AreDaemonsConnectedAsync(CancellationToken ct)
        {
            var response = await daemon.ExecuteCmdAnyAsync<string>(logger, EC.GetPeerCount, ct);

            return response.Error == null && response.Response.IntegralFromHex<uint>() > 0;
        }

        protected override async Task EnsureDaemonsSynchedAsync(CancellationToken ct)
        {
            var syncPendingNotificationShown = false;

            while (true)
            {
                var responses = await daemon.ExecuteCmdAllAsync<object>(logger, EC.GetSyncState, ct);

                var isSynched = responses.All(x => x.Error == null &&
                                                   x.Response is false);

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
                new DaemonCmd(EC.GetNetVersion),
                new DaemonCmd(EC.GetAccounts),
                new DaemonCmd(EC.GetCoinbase),
            };

            var results = await daemon.ExecuteBatchAnyAsync(logger, ct, commands);

            if (results.Any(x => x.Error != null))
            {
                var errors = results.Take(3).Where(x => x.Error != null)
                    .ToArray();

                if (errors.Any())
                    logger.ThrowLogPoolStartupException($"Init RPC failed: {string.Join(", ", errors.Select(y => y.Error.Message))}");
            }

            var netVersion = results[0].Response.ToObject<string>();
            var accounts = results[1].Response.ToObject<string[]>();
            var coinbase = results[2].Response.ToObject<string>();
            var gethChain = extraPoolConfig?.ChainTypeOverride ?? "Ethereum";

            EthereumUtils.DetectNetworkAndChain(netVersion, gethChain, out networkType, out chainType);

            BlockchainStats.RewardType = "POW";
            BlockchainStats.NetworkType = $"{chainType}-{networkType}";

            await UpdateNetworkStatsAsync(ct);

            Observable.Interval(TimeSpan.FromMinutes(10))
                .Select(via => Observable.FromAsync(() =>
                    Guard(() => UpdateNetworkStatsAsync(ct),
                        ex => logger.Error(ex))))
                .Concat()
                .Subscribe();

            if (poolConfig.EnableInternalStratum == true)
            {
                while (true)
                {
                    var blockTemplate = await GetBlockTemplateAsync(ct);

                    if (blockTemplate != null)
                    {
                        logger.Info(() => "Loading current DAG ...");

                        await ethash.GetDagAsync(blockTemplate.Height, logger, ct);

                        logger.Info(() => "Loaded current DAG");
                        break;
                    }

                    logger.Info(() => "Waiting for first valid block template");
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
            }

            ConfigureRewards();

            SetupJobUpdates(ct);
        }

        private void ConfigureRewards()
        {
            if (networkType == EthereumNetworkType.Mainnet &&
                chainType == GethChainType.Ethereum &&
                DevDonation.Addresses.TryGetValue(poolConfig.Template.As<CoinTemplate>().Symbol, out var address))
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

        protected virtual void SetupJobUpdates(CancellationToken cancellationToken)
        {
            var pollingInterval = poolConfig.BlockRefreshInterval > 0 ? poolConfig.BlockRefreshInterval : 1000;

            Jobs = Observable.Interval(TimeSpan.FromMilliseconds(pollingInterval))
                .Select(_ => Observable.FromAsync(UpdateJobAsync))
                .Concat()
                .Do(isNew =>
                {
                    if (isNew)
                        logger.Info(() => $"New work at height {currentJob.BlockTemplate.Height} and header {currentJob.BlockTemplate.Header} detected [{JobRefreshBy.Poll}]");
                })
                .Where(isNew => isNew)
                .Select(_ => GetJobParamsForStratum(true))
                .Publish()
                .RefCount();
        }

        #endregion // Overrides
    }
}