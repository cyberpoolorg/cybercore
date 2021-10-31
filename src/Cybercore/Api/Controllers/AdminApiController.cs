using Autofac;
using System;
using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Cybercore.Api.Extensions;
using Cybercore.Api.Responses;
using Cybercore.Blockchain;
using Cybercore.Configuration;
using Cybercore.Extensions;
using Cybercore.Mining;
using Cybercore.Persistence.Model;
using Cybercore.Persistence.Model.Projections;
using Cybercore.Persistence.Repositories;
using Cybercore.Time;
using Cybercore.Util;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using NLog;

namespace Cybercore.Api.Controllers
{
    [Route("api/admin")]
    [ApiController]
    public class AdminApiController : ApiControllerBase
    {
        public AdminApiController(IComponentContext ctx, IActionDescriptorCollectionProvider _adcp) : base(ctx)
        {
            gcStats = ctx.Resolve<Responses.AdminGcStats>();
            statsRepo = ctx.Resolve<IStatsRepository>();
            blocksRepo = ctx.Resolve<IBlockRepository>();
            minerRepo = ctx.Resolve<IMinerRepository>();
            shareRepo = ctx.Resolve<IShareRepository>();
            balanceRepo = ctx.Resolve<IBalanceRepository>();
            paymentsRepo = ctx.Resolve<IPaymentRepository>();
            clock = ctx.Resolve<IMasterClock>();
            pools = ctx.Resolve<ConcurrentDictionary<string, IMiningPool>>();
            adcp = _adcp;
        }

        private readonly Responses.AdminGcStats gcStats;
        private readonly IPaymentRepository paymentsRepo;
        private readonly IBalanceRepository balanceRepo;
        private readonly IMinerRepository minerRepo;
        private readonly IStatsRepository statsRepo;
        private readonly IBlockRepository blocksRepo;
        private readonly IShareRepository shareRepo;
        private readonly IMasterClock clock;
        private readonly ConcurrentDictionary<string, IMiningPool> pools;
        private readonly IActionDescriptorCollectionProvider adcp;

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        #region Actions

        [HttpGet("stats/gc")]
        public ActionResult<Responses.AdminGcStats> GetGcStats()
        {
            gcStats.GcGen0 = GC.CollectionCount(0);
            gcStats.GcGen1 = GC.CollectionCount(1);
            gcStats.GcGen2 = GC.CollectionCount(2);
            gcStats.MemAllocated = FormatUtil.FormatCapacity(GC.GetTotalMemory(false));

            return gcStats;
        }

        [HttpPost("forcegc")]
        public ActionResult<string> ForceGc()
        {
            GC.Collect(2, GCCollectionMode.Forced);
            return "Ok";
        }

        [HttpGet("{poolId}/miners")]
        public async Task<MinerPerformanceStats[]> PagePoolMinersAsync(
            string poolId, [FromQuery] int page, [FromQuery] int pageSize = 15)
        {
            var pool = GetPool(poolId);
            var end = clock.Now;
            var start = end.AddMinutes(-30);

            var miners = (await cf.Run(con => statsRepo.AdminPagePoolMinersByHashrateAsync(con, pool.Id, start, page, pageSize)))
                .Select(mapper.Map<MinerPerformanceStats>)
                .ToArray();

            return miners;
        }

        [HttpGet("pools/{poolId}/miners/{address}/getbalance")]
        public async Task<decimal> GetMinerBalanceAsync(string poolId, string address)
        {
            return await cf.Run(con => balanceRepo.GetBalanceAsync(con, poolId, address));
        }

        [HttpGet("pools/{poolId}/miners/{address}/settings")]
        public async Task<Responses.MinerSettings> GetMinerSettingsAsync(string poolId, string address)
        {
            var pool = GetPool(poolId);

            if (string.IsNullOrEmpty(address))
                throw new ApiException("Invalid or missing miner address", HttpStatusCode.NotFound);

            var result = await cf.Run(con => minerRepo.GetSettings(con, null, pool.Id, address));

            if (result == null)
                throw new ApiException("No settings found", HttpStatusCode.NotFound);

            return mapper.Map<Responses.MinerSettings>(result);
        }

        [HttpPost("pools/{poolId}/miners/{address}/settings")]
        public async Task<Responses.MinerSettings> SetMinerSettingsAsync(string poolId, string address,
            [FromBody] Responses.MinerSettings settings)
        {
            var pool = GetPool(poolId);

            if (string.IsNullOrEmpty(address))
                throw new ApiException("Invalid or missing miner address", HttpStatusCode.NotFound);

            if (settings == null)
                throw new ApiException("Invalid or missing settings", HttpStatusCode.BadRequest);

            var mapped = mapper.Map<Persistence.Model.MinerSettings>(settings);

            if (pool.PaymentProcessing != null)
                mapped.PaymentThreshold = Math.Max(mapped.PaymentThreshold, pool.PaymentProcessing.MinimumPayment);

            mapped.PoolId = pool.Id;
            mapped.Address = address;

            var result = await cf.RunTx(async (con, tx) =>
            {
                await minerRepo.UpdateSettings(con, tx, mapped);

                return await minerRepo.GetSettings(con, tx, mapped.PoolId, mapped.Address);
            });

            logger.Info(() => $"Updated settings for pool {pool.Id}, miner {address}");
            return mapper.Map<Responses.MinerSettings>(result);
        }

        #endregion // Actions

        private async Task<Responses.WorkerPerformanceStatsContainer[]> GetMinerPerformanceInternal(
            SampleRange mode, PoolConfig pool, string address)
        {
            Persistence.Model.Projections.WorkerPerformanceStatsContainer[] stats = null;
            var end = clock.Now;
            DateTime start;

            switch (mode)
            {
                case SampleRange.Hour:
                    end = end.AddSeconds(-end.Second);

                    start = end.AddHours(-1);

                    stats = await cf.Run(con => statsRepo.GetMinerPerformanceBetweenThreeMinutelyAsync(con, pool.Id, address, start, end));
                    break;

                case SampleRange.Day:
                    if (end.Minute < 30)
                        end = end.AddHours(-1);

                    end = end.AddMinutes(-end.Minute);
                    end = end.AddSeconds(-end.Second);

                    start = end.AddDays(-1);

                    stats = await cf.Run(con => statsRepo.GetMinerPerformanceBetweenHourlyAsync(con, pool.Id, address, start, end));
                    break;

                case SampleRange.Month:
                    if (end.Hour < 12)
                        end = end.AddDays(-1);

                    end = end.Date;

                    start = end.AddMonths(-1);

                    stats = await cf.Run(con => statsRepo.GetMinerPerformanceBetweenDailyAsync(con, pool.Id, address, start, end));
                    break;
            }

            var result = mapper.Map<Responses.WorkerPerformanceStatsContainer[]>(stats);
            return result;
        }
    }
}