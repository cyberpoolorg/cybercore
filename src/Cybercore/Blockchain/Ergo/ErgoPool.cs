using Autofac;
using AutoMapper;
using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Numerics;
using Cybercore.Blockchain.Bitcoin;
using Cybercore.Blockchain.Ergo.Configuration;
using Cybercore.Configuration;
using Cybercore.Extensions;
using Cybercore.JsonRpc;
using Cybercore.Messaging;
using Cybercore.Mining;
using Cybercore.Nicehash;
using Cybercore.Notifications.Messages;
using Cybercore.Persistence;
using Cybercore.Persistence.Repositories;
using Cybercore.Stratum;
using Cybercore.Time;
using Cybercore.Util;
using Newtonsoft.Json;
using static Cybercore.Util.ActionUtils;

namespace Cybercore.Blockchain.Ergo
{
    [CoinFamily(CoinFamily.Ergo)]
    public class ErgoPool : PoolBase
    {
        public ErgoPool(IComponentContext ctx,
            JsonSerializerSettings serializerSettings,
            IConnectionFactory cf,
            IStatsRepository statsRepo,
            IMapper mapper,
            IMasterClock clock,
            IMessageBus messageBus,
            NicehashService nicehashService) :
            base(ctx, serializerSettings, cf, statsRepo, mapper, clock, messageBus, nicehashService)
        {
        }

        protected object[] currentJobParams;
        protected ErgoJobManager manager;
        private ErgoPoolConfigExtra extraPoolConfig;
        private ErgoCoinTemplate coin;

        protected virtual async Task OnSubscribeAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            if (request.Id == null)
                throw new StratumException(StratumError.MinusOne, "missing request id");

            var context = connection.ContextAs<ErgoWorkerContext>();
            var requestParams = request.ParamsAs<string[]>();

            var data = new object[]
            {
                new object[]
                {
                    new object[] { BitcoinStratumMethods.SetDifficulty, connection.ConnectionId },
                    new object[] { BitcoinStratumMethods.MiningNotify, connection.ConnectionId }
                }
            }
            .Concat(manager.GetSubscriberData(connection))
            .ToArray();

            await connection.RespondAsync(data, request.Id);

            context.IsSubscribed = true;
            context.UserAgent = requestParams?.Length > 0 ? requestParams[0].Trim() : null;
        }

        protected virtual async Task OnAuthorizeAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
        {
            var request = tsRequest.Value;

            if (request.Id == null)
                throw new StratumException(StratumError.MinusOne, "missing request id");

            var context = connection.ContextAs<ErgoWorkerContext>();
            var requestParams = request.ParamsAs<string[]>();
            var workerValue = requestParams?.Length > 0 ? requestParams[0] : null;
            var password = requestParams?.Length > 1 ? requestParams[1] : null;
            var passParts = password?.Split(PasswordControlVarsSeparator);
            var split = workerValue?.Split('.');
            var minerName = split?.FirstOrDefault()?.Trim();
            var workerName = split?.Skip(1).FirstOrDefault()?.Trim() ?? string.Empty;

            context.IsAuthorized = await manager.ValidateAddress(minerName, ct);
            context.Miner = minerName;
            context.Worker = workerName;

            if (context.IsAuthorized)
            {
                await connection.RespondAsync(context.IsAuthorized, request.Id);

                logger.Info(() => $"[{connection.ConnectionId}] Authorized worker {workerValue}");

                var staticDiff = GetStaticDiffFromPassparts(passParts);

                var nicehashDiff = await GetNicehashStaticMinDiff(connection, context.UserAgent, coin.Name, coin.GetAlgorithmName());

                if (nicehashDiff.HasValue)
                {
                    if (!staticDiff.HasValue || nicehashDiff > staticDiff)
                    {
                        logger.Info(() => $"[{connection.ConnectionId}] Nicehash detected. Using API supplied difficulty of {nicehashDiff.Value}");

                        staticDiff = nicehashDiff;
                    }

                    else
                        logger.Info(() => $"[{connection.ConnectionId}] Nicehash detected. Using miner supplied difficulty of {staticDiff.Value}");
                }

                if (staticDiff.HasValue &&
                   (context.VarDiff != null && staticDiff.Value >= context.VarDiff.Config.MinDiff ||
                    context.VarDiff == null && staticDiff.Value > context.Difficulty))
                {
                    context.VarDiff = null;
                    context.SetDifficulty(staticDiff.Value);

                    logger.Info(() => $"[{connection.ConnectionId}] Setting static difficulty of {staticDiff.Value}");
                }

                await SendJob(connection, context, currentJobParams);
            }

            else
            {
                await connection.RespondErrorAsync(StratumError.UnauthorizedWorker, "Authorization failed", request.Id, context.IsAuthorized);

                logger.Info(() => $"[{connection.ConnectionId}] Banning unauthorized worker {minerName} for {loginFailureBanTimeout.TotalSeconds} sec");

                banManager.Ban(connection.RemoteEndpoint.Address, loginFailureBanTimeout);

                CloseConnection(connection);
            }
        }

        protected virtual async Task OnSubmitAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
        {
            var request = tsRequest.Value;
            var context = connection.ContextAs<ErgoWorkerContext>();

            try
            {
                if (request.Id == null)
                    throw new StratumException(StratumError.MinusOne, "missing request id");

                var requestAge = clock.Now - tsRequest.Timestamp.UtcDateTime;

                if (requestAge > maxShareAge)
                {
                    logger.Warn(() => $"[{connection.ConnectionId}] Dropping stale share submission request (server overloaded?)");
                    return;
                }

                context.LastActivity = clock.Now;

                if (!context.IsAuthorized)
                    throw new StratumException(StratumError.UnauthorizedWorker, "unauthorized worker");
                else if (!context.IsSubscribed)
                    throw new StratumException(StratumError.NotSubscribed, "not subscribed");

                var requestParams = request.ParamsAs<string[]>();
                var poolEndpoint = poolConfig.Ports[connection.LocalEndpoint.Port];

                var share = await manager.SubmitShareAsync(connection, requestParams, poolEndpoint.Difficulty, ct);

                await connection.RespondAsync(true, request.Id);

                messageBus.SendMessage(new StratumShare(connection, share));

                PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, true);

                logger.Info(() => $"[{connection.ConnectionId}] Share accepted: D={Math.Round(share.Difficulty * ErgoConstants.ShareMultiplier, 3)}");

                if (share.IsBlockCandidate)
                    poolStats.LastPoolBlockTime = clock.Now;

                context.Stats.ValidShares++;
                await UpdateVarDiffAsync(connection);
            }

            catch (StratumException ex)
            {
                PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, false);

                context.Stats.InvalidShares++;
                logger.Info(() => $"[{connection.ConnectionId}] Share rejected: {ex.Message} [{context.UserAgent}]");

                ConsiderBan(connection, context, poolConfig.Banning);

                throw;
            }
        }

        protected virtual Task OnNewJobAsync(object[] jobParams)
        {
            currentJobParams = jobParams;

            logger.Info(() => "Broadcasting job");

            return Guard(() => Task.WhenAll(ForEachConnection(async connection =>
             {
                 if (!connection.IsAlive)
                     return;

                 var context = connection.ContextAs<ErgoWorkerContext>();

                 if (!context.IsSubscribed || !context.IsAuthorized || CloseIfDead(connection, context))
                     return;

                 await SendJob(connection, context, currentJobParams);

             })), ex => logger.Debug(() => $"{nameof(OnNewJobAsync)}: {ex.Message}"));
        }

        private async Task SendJob(StratumConnection connection, ErgoWorkerContext context, object[] jobParams)
        {
            var jobParamsActual = new object[jobParams.Length];

            for (var i = 0; i < jobParamsActual.Length; i++)
                jobParamsActual[i] = jobParams[i];

            var target = new BigRational(BitcoinConstants.Diff1 * (BigInteger)(1 / context.Difficulty * 0x10000), 0x10000).GetWholePart();
            jobParamsActual[6] = target.ToString();

            await connection.NotifyAsync(BitcoinStratumMethods.SetDifficulty, new object[] { 1 });

            await connection.NotifyAsync(BitcoinStratumMethods.MiningNotify, jobParamsActual);
        }

        public override double HashrateFromShares(double shares, double interval)
        {
            var multiplier = BitcoinConstants.Pow2x32 * ErgoConstants.ShareMultiplier;
            var result = shares * multiplier / interval;

            result *= 1.15;

            return result;
        }

        public override double ShareMultiplier => ErgoConstants.ShareMultiplier;

        #region Overrides

        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            coin = poolConfig.Template.As<ErgoCoinTemplate>();
            extraPoolConfig = poolConfig.Extra.SafeExtensionDataAs<ErgoPoolConfigExtra>();

            base.Configure(poolConfig, clusterConfig);
        }

        protected override async Task SetupJobManager(CancellationToken ct)
        {
            var extraNonce1Size = extraPoolConfig?.ExtraNonce1Size ?? 2;

            manager = ctx.Resolve<ErgoJobManager>(
                new TypedParameter(typeof(IExtraNonceProvider), new ErgoExtraNonceProvider(poolConfig.Id, extraNonce1Size, clusterConfig.InstanceId)));

            manager.Configure(poolConfig, clusterConfig);

            await manager.StartAsync(ct);

            if (poolConfig.EnableInternalStratum == true)
            {
                disposables.Add(manager.Jobs
                    .Select(job => Observable.FromAsync(() =>
                        Guard(() => OnNewJobAsync(job),
                            ex => logger.Debug(() => $"{nameof(OnNewJobAsync)}: {ex.Message}"))))
                    .Concat()
                    .Subscribe(_ => { }, ex =>
                    {
                        logger.Debug(ex, nameof(OnNewJobAsync));
                    }));

                await manager.Jobs.Take(1).ToTask(ct);
            }

            else
            {
                disposables.Add(manager.Jobs.Subscribe());
            }
        }

        protected override async Task InitStatsAsync()
        {
            await base.InitStatsAsync();

            blockchainStats = manager.BlockchainStats;
        }

        protected override WorkerContextBase CreateWorkerContext()
        {
            return new ErgoWorkerContext();
        }

        protected override async Task OnRequestAsync(StratumConnection connection,
            Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
        {
            var request = tsRequest.Value;

            try
            {
                switch (request.Method)
                {
                    case BitcoinStratumMethods.Subscribe:
                        await OnSubscribeAsync(connection, tsRequest);
                        break;

                    case BitcoinStratumMethods.Authorize:
                        await OnAuthorizeAsync(connection, tsRequest, ct);
                        break;

                    case BitcoinStratumMethods.SubmitShare:
                        await OnSubmitAsync(connection, tsRequest, ct);
                        break;

                    default:
                        logger.Debug(() => $"[{connection.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                        await connection.RespondErrorAsync(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                        break;
                }
            }

            catch (StratumException ex)
            {
                await connection.RespondErrorAsync(ex.Code, ex.Message, request.Id, false);
            }
        }

        protected override async Task<double?> GetNicehashStaticMinDiff(StratumConnection connection, string userAgent, string coinName, string algoName)
        {
            var result = await base.GetNicehashStaticMinDiff(connection, userAgent, coinName, algoName);

            if (result.HasValue)
                result = result.Value / uint.MaxValue;

            return result;
        }

        protected override async Task OnVarDiffUpdateAsync(StratumConnection connection, double newDiff)
        {
            var context = connection.ContextAs<ErgoWorkerContext>();

            context.EnqueueNewDifficulty(newDiff);

            if (context.HasPendingDifficulty)
            {
                context.ApplyPendingDifficulty();

                await SendJob(connection, context, currentJobParams);
            }
        }

        #endregion // Overrides
    }
}
