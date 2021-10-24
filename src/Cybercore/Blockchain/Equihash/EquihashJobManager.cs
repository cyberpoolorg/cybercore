using Autofac;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cybercore.Blockchain.Bitcoin;
using Cybercore.Blockchain.Bitcoin.DaemonResponses;
using Cybercore.Blockchain.Equihash.Custom.BitcoinGold;
using Cybercore.Blockchain.Equihash.Custom.Minexcoin;
using Cybercore.Blockchain.Equihash.Custom.VerusCoin;
using Cybercore.Blockchain.Equihash.DaemonResponses;
using Cybercore.Configuration;
using Cybercore.Contracts;
using Cybercore.Crypto.Hashing.Equihash;
using Cybercore.DaemonInterface;
using Cybercore.Extensions;
using Cybercore.JsonRpc;
using Cybercore.Messaging;
using Cybercore.Stratum;
using Cybercore.Time;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;
using NLog;

namespace Cybercore.Blockchain.Equihash
{
    public class EquihashJobManager : BitcoinJobManagerBase<EquihashJob>
    {
        public EquihashJobManager(
            IComponentContext ctx,
            IMasterClock clock,
            IMessageBus messageBus,
            IExtraNonceProvider extraNonceProvider) : base(ctx, clock, messageBus, extraNonceProvider)
        {
        }

        private EquihashCoinTemplate coin;
        private EquihashSolver solver;
        public EquihashCoinTemplate.EquihashNetworkParams ChainConfig { get; private set; }

        protected override void PostChainIdentifyConfigure()
        {
            ChainConfig = coin.GetNetwork(network.ChainName);
            solver = EquihashSolverFactory.GetSolver(ctx, ChainConfig.Solver);

            base.PostChainIdentifyConfigure();
        }

        private async Task<DaemonResponse<EquihashBlockTemplate>> GetBlockTemplateAsync(CancellationToken ct)
        {
            logger.LogInvoke();

            var subsidyResponse = await daemon.ExecuteCmdAnyAsync<ZCashBlockSubsidy>(logger, BitcoinCommands.GetBlockSubsidy, ct);

            var result = await daemon.ExecuteCmdAnyAsync<EquihashBlockTemplate>(logger,
                BitcoinCommands.GetBlockTemplate, ct, extraPoolConfig?.GBTArgs ?? (object)GetBlockTemplateParams());

            if (subsidyResponse.Error == null && result.Error == null && result.Response != null)
                result.Response.Subsidy = subsidyResponse.Response;
            else if (subsidyResponse.Error?.Code != (int)BitcoinRPCErrorCode.RPC_METHOD_NOT_FOUND)
                result.Error = new JsonRpcException(-1, $"{BitcoinCommands.GetBlockSubsidy} failed", null);

            return result;
        }

        private DaemonResponse<EquihashBlockTemplate> GetBlockTemplateFromJson(string json)
        {
            logger.LogInvoke();

            var result = JsonConvert.DeserializeObject<JsonRpcResponse>(json);

            return new DaemonResponse<EquihashBlockTemplate>
            {
                Response = result.ResultAs<EquihashBlockTemplate>(),
            };
        }

        protected override IDestination AddressToDestination(string address, BitcoinAddressType? addressType)
        {
            if (!coin.UsesZCashAddressFormat)
                return base.AddressToDestination(address, addressType);

            var decoded = Encoders.Base58.DecodeData(address);
            var hash = decoded.Skip(2).Take(20).ToArray();
            var result = new KeyId(hash);
            return result;
        }

        private EquihashJob CreateJob()
        {
            switch (coin.Symbol)
            {
                case "BTG":
                    return new BitcoinGoldJob();

                case "MNX":
                    return new MinexcoinJob();

                case "VRSC":
                    return new VerusCoinJob();
            }
            return new EquihashJob();
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
                        poolConfig, clusterConfig, clock, poolAddressDestination, network, solver);

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
            coin = poolConfig.Template.As<EquihashCoinTemplate>();

            base.Configure(poolConfig, clusterConfig);
        }

        public override async Task<bool> ValidateAddressAsync(string address, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(address))
                return false;

            if (await base.ValidateAddressAsync(address, ct))
                return true;

            var result = await daemon.ExecuteCmdAnyAsync<ValidateAddressResponse>(logger, ct,
                EquihashCommands.ZValidateAddress, new[] { address });

            return result.Response is { IsValid: true };
        }

        public object[] GetSubscriberData(StratumConnection worker)
        {
            Contract.RequiresNonNull(worker, nameof(worker));

            var context = worker.ContextAs<BitcoinWorkerContext>();

            context.ExtraNonce1 = extraNonceProvider.Next();

            var responseData = new object[]
            {
                context.ExtraNonce1
            };

            return responseData;
        }

        public async ValueTask<Share> SubmitShareAsync(StratumConnection worker, object submission,
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
            var nTime = submitParams[2] as string;
            var extraNonce2 = submitParams[3] as string;
            var solution = submitParams[4] as string;

            if (string.IsNullOrEmpty(workerValue))
                throw new StratumException(StratumError.Other, "missing or invalid workername");

            if (string.IsNullOrEmpty(solution))
                throw new StratumException(StratumError.Other, "missing or invalid solution");

            EquihashJob job;

            lock (jobLock)
            {
                job = validJobs.FirstOrDefault(x => x.JobId == jobId);
            }

            if (job == null)
                throw new StratumException(StratumError.JobNotFound, "job not found");

            var (share, blockHex) = job.ProcessShare(worker, extraNonce2, nTime, solution);

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

            share.PoolId = poolConfig.Id;
            share.IpAddress = worker.RemoteEndpoint.Address.ToString();
            share.Miner = context.Miner;
            share.Worker = context.Worker;
            share.UserAgent = context.UserAgent;
            share.Source = clusterConfig.ClusterName;
            share.NetworkDifficulty = job.Difficulty;
            share.Difficulty = share.Difficulty;
            share.Created = clock.Now;

            return share;
        }


        #endregion // API-Surface
    }
}