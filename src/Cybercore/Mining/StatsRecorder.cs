using Autofac;
using AutoMapper;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Net.Sockets;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cybercore.Blockchain;
using Cybercore.Configuration;
using Cybercore.Contracts;
using Cybercore.Extensions;
using Cybercore.Messaging;
using Cybercore.Notifications.Messages;
using Cybercore.Persistence;
using Cybercore.Persistence.Model;
using Cybercore.Persistence.Repositories;
using Cybercore.Time;
using Cybercore.Util;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using NLog;
using Polly;

namespace Cybercore.Mining
{
    public class StatsRecorder : BackgroundService
    {
        public StatsRecorder(
            IComponentContext ctx,
            IMasterClock clock,
            IConnectionFactory cf,
            IMessageBus messageBus,
            IMapper mapper,
            ClusterConfig clusterConfig,
            IShareRepository shareRepo,
            IStatsRepository statsRepo,
            IBalanceRepository balanceRepo,
            IBlockRepository blockRepo)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));
            Contract.RequiresNonNull(mapper, nameof(mapper));
            Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
            Contract.RequiresNonNull(statsRepo, nameof(statsRepo));
            Contract.RequiresNonNull(balanceRepo, nameof(balanceRepo));
            Contract.RequiresNonNull(blockRepo, nameof(blockRepo));

            this.clock = clock;
            this.cf = cf;
            this.mapper = mapper;
            this.messageBus = messageBus;
            this.shareRepo = shareRepo;
            this.statsRepo = statsRepo;
            this.balanceRepo = balanceRepo;
            this.blockRepo = blockRepo;
            this.clusterConfig = clusterConfig;

            updateInterval = TimeSpan.FromSeconds(clusterConfig.Statistics?.UpdateInterval ?? 60);
            gcInterval = TimeSpan.FromMinutes(clusterConfig.Statistics?.GcInterval ?? 1);
            hashrateCalculationWindow = TimeSpan.FromMinutes(clusterConfig.Statistics?.HashrateCalculationWindow ?? 5);
            cleanupDays = TimeSpan.FromDays(clusterConfig.Statistics?.CleanupDays ?? 180);

            BuildFaultHandlingPolicy();
        }

        private readonly IMasterClock clock;
        private readonly IConnectionFactory cf;
        private readonly IMapper mapper;
        private readonly IMessageBus messageBus;
        private readonly IShareRepository shareRepo;
        private readonly IStatsRepository statsRepo;
        private readonly IBalanceRepository balanceRepo;
        private readonly IBlockRepository blockRepo;
        private readonly ClusterConfig clusterConfig;
        private readonly CompositeDisposable disposables = new();
        private readonly ConcurrentDictionary<string, IMiningPool> pools = new();
        private readonly TimeSpan updateInterval;
        private readonly TimeSpan gcInterval;
        private readonly TimeSpan hashrateCalculationWindow;
        private readonly TimeSpan cleanupDays;
        private const double HashrateBoostFactor = 1.1d;
        private const int RetryCount = 4;
        private IAsyncPolicy readFaultPolicy;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        protected BlockchainStats blockchainStats;

        private void AttachPool(IMiningPool pool)
        {
            pools.TryAdd(pool.Config.Id, pool);
        }

        private void OnPoolStatusNotification(PoolStatusNotification notification)
        {
            if (notification.Status == PoolStatus.Online)
                AttachPool(notification.Pool);
        }

        private async Task UpdatePoolHashratesAsync(CancellationToken ct)
        {
            var now = clock.Now;
            var timeFrom = now.Add(-hashrateCalculationWindow);
            var from = DateTime.MinValue;
            var to = clock.Now;
            var poolShares = (double)0;
            var netDiff = (double)0;

            var stats = new MinerWorkerPerformanceStats
            {
                Created = now
            };

            foreach (var poolId in pools.Keys)
            {
                if (ct.IsCancellationRequested)
                    return;

                stats.PoolId = poolId;

                logger.Info(() => $"[{poolId}] Updating Statistics for pool");

                var pool = pools[poolId];

                var result = await readFaultPolicy.ExecuteAsync(() =>
                    cf.Run(con => shareRepo.GetHashAccumulationBetweenCreatedAsync(con, poolId, timeFrom, now)));

                var byMiner = result.GroupBy(x => x.Miner).ToArray();

                var lastBlock = await cf.Run(con => blockRepo.GetBlockBeforeAsync(con, poolId, new[]
                {
                BlockStatus.Confirmed,
                BlockStatus.Orphaned,
                BlockStatus.Pending,
                }, to));

                if (lastBlock != null)
                    from = lastBlock.Created;

                var accumulatedShareDiffForBlock = await cf.Run(con => shareRepo.GetAccumulatedShareDifficultyBetweenCreatedAsync(con, poolId, from, to));

                if (accumulatedShareDiffForBlock.HasValue)
                    poolShares = accumulatedShareDiffForBlock.Value;

                var diff = await cf.Run(con => statsRepo.GetLastPoolStatsAsync(con, poolId));

                if (diff != null)
                    netDiff = diff.NetworkDifficulty;

                if (result.Length > 0)
                {
                    var workerCount = 0;
                    foreach (var workers in byMiner)
                    {
                        workerCount += workers.Count();
                    }

                    var timeFrameBeforeFirstShare = ((result.Min(x => x.FirstShare) - timeFrom).TotalSeconds);
                    var timeFrameAfterLastShare = ((now - result.Max(x => x.LastShare)).TotalSeconds);
                    var timeFrameFirstLastShare = (hashrateCalculationWindow.TotalSeconds - timeFrameBeforeFirstShare - timeFrameAfterLastShare);
                    var poolHashTimeFrame = hashrateCalculationWindow.TotalSeconds;
                    var poolHashesAccumulated = result.Sum(x => x.Sum);
                    var poolHashesCountAccumulated = result.Sum(x => x.Count);
                    var poolHashrate = pool.HashrateFromShares(poolHashesAccumulated, poolHashTimeFrame) * HashrateBoostFactor;

                    poolHashrate = Math.Round(poolHashrate, 8);

                    if (poolId == "idx" || poolId == "vgc" || poolId == "shrx" || poolId == "ecc" || poolId == "gold" || poolId == "eli" || poolId == "acm" || poolId == "alps" || poolId == "grs")
                    {
                        poolHashrate *= 11.2;
                    }

                    if (poolId == "rng")
                    {
                        poolHashrate *= 2850;
                    }

                    if (poolId == "lccm" || poolId == "lccms")
                    {
                        poolHashrate *= 1100000;
                        logger.Info(() => $"[{poolId}] Pool Hashrate Adjusted {poolHashrate}");
                    }

                    if (poolId == "sugar")
                    {
                        poolHashrate *= 58.5;
                    }

                    pool.PoolStats.ConnectedMiners = byMiner.Length;
                    pool.PoolStats.ConnectedWorkers = workerCount;
                    pool.PoolStats.PoolHashrate = (ulong)poolHashrate;
                    pool.PoolStats.SharesPerSecond = (double)(poolHashesCountAccumulated / poolHashTimeFrame);
                    pool.PoolStats.RoundShares = (double)poolShares;
                    pool.PoolStats.RoundEffort = (poolShares / netDiff) * 100;

                    messageBus.NotifyHashrateUpdated(pool.Config.Id, poolHashrate);

                }
                else
                {

                    pool.PoolStats.ConnectedMiners = 0;
                    pool.PoolStats.ConnectedWorkers = 0;
                    pool.PoolStats.PoolHashrate = 0;
                    pool.PoolStats.SharesPerSecond = 0;
                    pool.PoolStats.RoundShares = 0;
                    pool.PoolStats.RoundEffort = 0;

                    messageBus.NotifyHashrateUpdated(pool.Config.Id, 0);

                    logger.Info(() => $"[{poolId}] Reset performance stats for pool");
                }

                await cf.RunTx(async (con, tx) =>
                {
                    var mapped = new Persistence.Model.PoolStats
                    {
                        PoolId = poolId,
                        Created = now
                    };

                    mapper.Map(pool.PoolStats, mapped);
                    mapper.Map(pool.NetworkStats, mapped);

                    await statsRepo.InsertPoolStatsAsync(con, tx, mapped);
                });

                var previousMinerWorkerHashrates = await cf.Run(con =>
                    statsRepo.GetPoolMinerWorkerHashratesAsync(con, poolId));

                const char keySeparator = '.';

                string BuildKey(string miner, string worker = null)
                {
                    return !string.IsNullOrEmpty(worker) ? $"{miner}{keySeparator}{worker}" : miner;
                }

                var previousNonZeroMinerWorkers = new HashSet<string>(
                    previousMinerWorkerHashrates.Select(x => BuildKey(x.Miner, x.Worker)));

                var currentNonZeroMinerWorkers = new HashSet<string>();

                foreach (var minerHashes in byMiner)
                {
                    if (ct.IsCancellationRequested)
                        return;

                    double minerTotalHashrate = 0;

                    await cf.RunTx(async (con, tx) =>
                    {
                        stats.Miner = minerHashes.Key;

                        currentNonZeroMinerWorkers.Add(BuildKey(stats.Miner));

                        foreach (var item in minerHashes)
                        {
                            stats.Hashrate = 0;
                            stats.SharesPerSecond = 0;

		            var minerIp = await cf.Run(con => shareRepo.GetRecentyUsedIpAddress(con, poolId, minerHashes.Key));
		            var minerSource = await cf.Run(con => shareRepo.GetRecentyUsedSource(con, poolId, minerHashes.Key));
		            var minerBalance = await cf.Run(con => balanceRepo.GetBalanceAsync(con, poolId, minerHashes.Key));
                            var timeFrameBeforeFirstShare = ((minerHashes.Min(x => x.FirstShare) - timeFrom).TotalSeconds);
                            var timeFrameAfterLastShare = ((now - minerHashes.Max(x => x.LastShare)).TotalSeconds);

                            var minerHashTimeFrame = hashrateCalculationWindow.TotalSeconds;

                            if (timeFrameBeforeFirstShare >= (hashrateCalculationWindow.TotalSeconds * 0.1))
                                minerHashTimeFrame = Math.Floor(hashrateCalculationWindow.TotalSeconds - timeFrameBeforeFirstShare);

                            if (timeFrameAfterLastShare >= (hashrateCalculationWindow.TotalSeconds * 0.1))
                                minerHashTimeFrame = Math.Floor(hashrateCalculationWindow.TotalSeconds + timeFrameAfterLastShare);

                            if ((timeFrameBeforeFirstShare >= (hashrateCalculationWindow.TotalSeconds * 0.1)) && (timeFrameAfterLastShare >= (hashrateCalculationWindow.TotalSeconds * 0.1)))
                                minerHashTimeFrame = (hashrateCalculationWindow.TotalSeconds - timeFrameBeforeFirstShare + timeFrameAfterLastShare);

                            if (minerHashTimeFrame < 1)
                                minerHashTimeFrame = 1;

                            var minerHashrate = pool.HashrateFromShares(item.Sum, minerHashTimeFrame) * HashrateBoostFactor;

                            minerHashrate = Math.Round(minerHashrate, 8);

                            if (poolId == "idx" || poolId == "vgc" || poolId == "shrx" || poolId == "ecc" || poolId == "gold" || poolId == "eli" || poolId == "acm" || poolId == "alps" || poolId == "grs")
                            {
                                minerHashrate *= 11.2;
                            }

                            if (poolId == "rng" || poolId == "lccm" || poolId == "lccms")
                            {
                                minerHashrate *= 2850;
                            }

                            if (poolId == "lccm" || poolId == "lccms")
                            {
                                minerHashrate *= 400;
                            }

                            if (poolId == "sugar")
                            {
                                minerHashrate *= 58.5;
                            }

                            minerTotalHashrate += minerHashrate;

                            stats.Hashrate = minerHashrate;
                            stats.Worker = item.Worker;
                            stats.SharesPerSecond = Math.Round(item.Count / minerHashTimeFrame, 4);
			    stats.IpAddress = minerIp;
			    stats.Source = minerSource;
			    stats.Balance = minerBalance;

                            await statsRepo.InsertMinerWorkerPerformanceStatsAsync(con, tx, stats);

                            messageBus.NotifyHashrateUpdated(pool.Config.Id, minerHashrate, stats.Miner, stats.Worker);

                            logger.Info(() => $"[{poolId}] Worker {stats.Miner}{(!string.IsNullOrEmpty(stats.Worker) ? $".{stats.Worker}" : string.Empty)}: {FormatUtil.FormatHashrate(minerHashrate)}, {stats.SharesPerSecond} shares/sec");

                            currentNonZeroMinerWorkers.Add(BuildKey(stats.Miner, stats.Worker));
                        }
                    });

                    messageBus.NotifyHashrateUpdated(pool.Config.Id, minerTotalHashrate, stats.Miner, null);

                    logger.Info(() => $"[{poolId}] Miner {stats.Miner}: {FormatUtil.FormatHashrate(minerTotalHashrate)}");
                }

                var orphanedHashrateForMinerWorker = previousNonZeroMinerWorkers.Except(currentNonZeroMinerWorkers).ToArray();

                if (orphanedHashrateForMinerWorker.Any())
                {
                    async Task Action(IDbConnection con, IDbTransaction tx)
                    {
                        stats.Hashrate = 0;
                        stats.SharesPerSecond = 0;

                        foreach (var item in orphanedHashrateForMinerWorker)
                        {
                            var parts = item.Split(keySeparator);
                            var miner = parts[0];
                            var worker = parts.Length > 1 ? parts[1] : null;

                            stats.Miner = miner;
                            stats.Worker = worker;

                            await statsRepo.InsertMinerWorkerPerformanceStatsAsync(con, tx, stats);

                            messageBus.NotifyHashrateUpdated(pool.Config.Id, 0, stats.Miner, stats.Worker);

                            if (string.IsNullOrEmpty(stats.Worker))
                                logger.Info(() => $"[{poolId}] Reset performance stats for miner {stats.Miner}");
                            else
                                logger.Info(() => $"[{poolId}] Reset performance stats for miner {stats.Miner}.{stats.Worker}");
                        }
                    }

                    await cf.RunTx(Action);
                }
            }
        }

        private async Task StatsGcAsync(CancellationToken ct)
        {
            logger.Info(() => "Performing Stats GC");

            await cf.Run(async con =>
            {
                var cutOff = clock.Now.Add(-cleanupDays);

                var rowCount = await statsRepo.DeletePoolStatsBeforeAsync(con, cutOff);
                if (rowCount > 0)
                    logger.Info(() => $"Deleted {rowCount} old poolstats records");

                rowCount = await statsRepo.DeleteMinerStatsBeforeAsync(con, cutOff);
                if (rowCount > 0)
                    logger.Info(() => $"Deleted {rowCount} old minerstats records");
            });

            logger.Info(() => "Stats GC complete");
        }

        private async Task UpdateAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await UpdatePoolHashratesAsync(ct);
                }

                catch (Exception ex)
                {
                    logger.Error(ex);
                }

                await Task.Delay(updateInterval, ct);
            }
        }

        private async Task GcAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await StatsGcAsync(ct);
                }

                catch (Exception ex)
                {
                    logger.Error(ex);
                }

                await Task.Delay(gcInterval, ct);
            }
        }

        private void BuildFaultHandlingPolicy()
        {
            var retry = Policy
                .Handle<DbException>()
                .Or<SocketException>()
                .Or<TimeoutException>()
                .RetryAsync(RetryCount, OnPolicyRetry);

            readFaultPolicy = retry;
        }

        private static void OnPolicyRetry(Exception ex, int retry, object context)
        {
            logger.Warn(() => $"Retry {retry} due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            try
            {
                disposables.Add(messageBus.Listen<PoolStatusNotification>()
                    .ObserveOn(TaskPoolScheduler.Default)
                    .Subscribe(OnPoolStatusNotification));

                logger.Info(() => "Online");

                await Task.Delay(TimeSpan.FromSeconds(15), ct);

                await Task.WhenAll(
                    UpdateAsync(ct),
                    GcAsync(ct));

                logger.Info(() => "Offline");
            }

            finally
            {
                disposables.Dispose();
            }
        }
    }
}