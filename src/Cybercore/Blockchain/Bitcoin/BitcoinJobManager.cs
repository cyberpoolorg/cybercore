using Autofac;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cybercore.Blockchain.Bitcoin.Configuration;
using Cybercore.Blockchain.Bitcoin.DaemonResponses;
using Cybercore.Configuration;
using Cybercore.Contracts;
using Cybercore.Crypto;
using Cybercore.DaemonInterface;
using Cybercore.Extensions;
using Cybercore.JsonRpc;
using Cybercore.Messaging;
using Cybercore.Stratum;
using Cybercore.Time;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace Cybercore.Blockchain.Bitcoin
{
    public class BitcoinJobManager : BitcoinJobManagerBase<BitcoinJob>
    {
        public BitcoinJobManager(
            IComponentContext ctx,
            IMasterClock clock,
            IMessageBus messageBus,
            IExtraNonceProvider extraNonceProvider) :
            base(ctx, clock, messageBus, extraNonceProvider)
        {
        }

        private BitcoinTemplate coin;

        protected override object[] GetBlockTemplateParams()
        {
            var result = base.GetBlockTemplateParams();

            if (coin.BlockTemplateRpcExtraParams != null)
            {
                if (coin.BlockTemplateRpcExtraParams.Type == JTokenType.Array)
                    result = result.Concat(coin.BlockTemplateRpcExtraParams.ToObject<object[]>() ?? Array.Empty<object>()).ToArray();
                else
                    result = result.Concat(new[] { coin.BlockTemplateRpcExtraParams.ToObject<object>() }).ToArray();
            }

            return result;
        }

        protected async Task<DaemonResponse<BlockTemplate>> GetBlockTemplateAsync(CancellationToken ct)
        {
            logger.LogInvoke();

            var result = await daemon.ExecuteCmdAnyAsync<BlockTemplate>(logger,
                BitcoinCommands.GetBlockTemplate, ct, extraPoolConfig?.GBTArgs ?? (object)GetBlockTemplateParams());

            return result;
        }

        protected DaemonResponse<BlockTemplate> GetBlockTemplateFromJson(string json)
        {
            logger.LogInvoke();

            var result = JsonConvert.DeserializeObject<JsonRpcResponse>(json);

            return new DaemonResponse<BlockTemplate>
            {
                Response = result.ResultAs<BlockTemplate>(),
            };
        }

        private BitcoinJob CreateJob()
        {
            return new();
        }

        protected override void PostChainIdentifyConfigure()
        {
            base.PostChainIdentifyConfigure();

            if (poolConfig.EnableInternalStratum == true && coin.HeaderHasherValue is IHashAlgorithmInit hashInit)
            {
                if (!hashInit.DigestInit(poolConfig))
                    logger.Error(() => $"{hashInit.GetType().Name} initialization failed");
            }
        }

        protected override async Task<(bool IsNew, bool Force)> UpdateJob(CancellationToken ct, bool forceUpdate, string via = null, string json = null)
        {
            logger.LogInvoke();

            try
            {
                if (forceUpdate)
                    lastJobRebroadcast = clock.Now;

                var response = string.IsNullOrEmpty(json) ?
                    await GetBlockTemplateAsync(ct) :
                    GetBlockTemplateFromJson(json);

                if (response.Error != null)
                {
                    logger.Warn(() => $"Unable to update job. Daemon responded with: {response.Error.Message} Code {response.Error.Code}");
                    return (false, forceUpdate);
                }

                var blockTemplate = response.Response;
                var job = currentJob;

                var isNew = job == null ||
                    (blockTemplate != null &&
                        (job.BlockTemplate?.PreviousBlockhash != blockTemplate.PreviousBlockhash ||
                        blockTemplate.Height > job.BlockTemplate?.Height));

                if (isNew)
                    messageBus.NotifyChainHeight(poolConfig.Id, blockTemplate.Height, poolConfig.Template);

                if (isNew || forceUpdate)
                {
                    job = CreateJob();

                    job.Init(blockTemplate, NextJobId(),
                        poolConfig, extraPoolConfig, clusterConfig, clock, poolAddressDestination, network, isPoS,
                        ShareMultiplier, coin.CoinbaseHasherValue, coin.HeaderHasherValue, coin.BlockHasherValue);

                    lock (jobLock)
                    {
                        validJobs.Insert(0, job);

                        while (validJobs.Count > maxActiveJobs)
                            validJobs.RemoveAt(validJobs.Count - 1);
                    }

                    if (isNew)
                    {
                        if (via != null)
                            logger.Info(() => $"Detected new block {blockTemplate.Height} [{via}]");
                        else
                            logger.Info(() => $"Detected new block {blockTemplate.Height}");

                        BlockchainStats.LastNetworkBlockTime = clock.Now;
                        BlockchainStats.BlockHeight = blockTemplate.Height;
                        BlockchainStats.NetworkDifficulty = job.Difficulty;
                        BlockchainStats.NextNetworkTarget = blockTemplate.Target;
                        BlockchainStats.NextNetworkBits = blockTemplate.Bits;
                        BlockchainStats.BlockReward = (double)blockTemplate.CoinbaseValue / 100000000;
                    }

                    else
                    {
                        if (via != null)
                            logger.Debug(() => $"Template update {blockTemplate.Height} [{via}]");
                        else
                            logger.Debug(() => $"Template update {blockTemplate.Height}");
                    }

                    currentJob = job;
                }

                return (isNew, forceUpdate);
            }

            catch (Exception ex)
            {
                logger.Error(ex, () => $"Error during {nameof(UpdateJob)}");
            }

            return (false, forceUpdate);
        }

        protected override object GetJobParamsForStratum(bool isNew)
        {
            var job = currentJob;
            return job?.GetJobParams(isNew);
        }

        #region API-Surface

        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            coin = poolConfig.Template.As<BitcoinTemplate>();
            extraPoolConfig = poolConfig.Extra.SafeExtensionDataAs<BitcoinPoolConfigExtra>();
            extraPoolPaymentProcessingConfig = poolConfig.PaymentProcessing?.Extra?.SafeExtensionDataAs<BitcoinPoolPaymentProcessingConfigExtra>();

            if (extraPoolConfig?.MaxActiveJobs.HasValue == true)
                maxActiveJobs = extraPoolConfig.MaxActiveJobs.Value;

            hasLegacyDaemon = extraPoolConfig?.HasLegacyDaemon == true;

            base.Configure(poolConfig, clusterConfig);
        }

        public virtual object[] GetSubscriberData(StratumConnection worker)
        {
            Contract.RequiresNonNull(worker, nameof(worker));

            var context = worker.ContextAs<BitcoinWorkerContext>();

            context.ExtraNonce1 = extraNonceProvider.Next();

            var responseData = new object[]
            {
                context.ExtraNonce1,
                BitcoinConstants.ExtranoncePlaceHolderLength - ExtranonceBytes,
            };

            return responseData;
        }

        public virtual async ValueTask<Share> SubmitShareAsync(StratumConnection worker, object submission,
            double stratumDifficultyBase, CancellationToken ct)
        {
            Contract.RequiresNonNull(worker, nameof(worker));
            Contract.RequiresNonNull(submission, nameof(submission));

            logger.LogInvoke(new object[] { worker.ConnectionId });

            if (submission is not object[] submitParams)
                throw new StratumException(StratumError.Other, "invalid params");

            var context = worker.ContextAs<BitcoinWorkerContext>();

            var workerValue = (submitParams[0] as string)?.Trim();
            var jobId = submitParams[1] as string;
            var extraNonce2 = submitParams[2] as string;
            var nTime = submitParams[3] as string;
            var nonce = submitParams[4] as string;
            var versionBits = context.VersionRollingMask.HasValue ? submitParams[5] as string : null;

            if (string.IsNullOrEmpty(workerValue))
                throw new StratumException(StratumError.Other, "missing or invalid workername");

            BitcoinJob job;

            lock (jobLock)
            {
                job = validJobs.FirstOrDefault(x => x.JobId == jobId);
            }

            if (job == null)
                throw new StratumException(StratumError.JobNotFound, "job not found");

            var (share, blockHex) = job.ProcessShare(worker, extraNonce2, nTime, nonce, versionBits);

            share.PoolId = poolConfig.Id;
            share.IpAddress = worker.RemoteEndpoint.Address.ToString();
            share.Miner = context.Miner;
            share.Worker = context.Worker;
            share.UserAgent = context.UserAgent;
            share.Source = clusterConfig.ClusterName;
            share.Created = clock.Now;

            if (share.IsBlockCandidate)
            {
                logger.Info(() => $"Submitting block {share.BlockHeight} [{share.BlockHash}]");

                var acceptResponse = await SubmitBlockAsync(share, blockHex);

                share.IsBlockCandidate = acceptResponse.Accepted;

                if (share.IsBlockCandidate)
                {
                    logger.Info(() => $"Daemon accepted block {share.BlockHeight} [{share.BlockHash}] submitted by {context.Miner}");

                    OnBlockFound();

                    share.TransactionConfirmationData = acceptResponse.CoinbaseTx;
                }

                else
                {
                    share.TransactionConfirmationData = null;
                }
            }

            return share;
        }

        public double ShareMultiplier => coin.ShareMultiplier;

        #endregion // API-Surface
    }
}