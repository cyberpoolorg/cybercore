using AutoMapper;
using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Cybercore.Extensions;
using Cybercore.Persistence.Model;
using Cybercore.Persistence.Model.Projections;
using Cybercore.Persistence.Repositories;
using Cybercore.Time;
using Dapper;
using Newtonsoft.Json;
using NLog;
using MinerStats = Cybercore.Persistence.Model.Projections.MinerStats;

namespace Cybercore.Persistence.Postgres.Repositories
{
    public class StatsRepository : IStatsRepository
    {
        public StatsRepository(IMapper mapper, IMasterClock clock)
        {
            this.mapper = mapper;
            this.clock = clock;
        }

        private readonly IMapper mapper;
        private readonly IMasterClock clock;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private static readonly TimeSpan MinerStatsMaxAge = TimeSpan.FromMinutes(20);

        public async Task InsertPoolStatsAsync(IDbConnection con, IDbTransaction tx, PoolStats stats)
        {
            logger.LogInvoke();

            var mapped = mapper.Map<Entities.PoolStats>(stats);

            const string query = "INSERT INTO poolstats(poolid, connectedminers, connectedworkers, poolhashrate, networkhashrate, networkdifficulty, " +
                "lastnetworkblocktime, lastpoolblocktime, blockheight, connectedpeers, sharespersecond, roundshares, roundeffort, created) " +
                "VALUES(@poolid, @connectedminers, @connectedworkers, @poolhashrate, @networkhashrate, @networkdifficulty, @lastnetworkblocktime, " +
                "@lastpoolblocktime, @blockheight, @connectedpeers, @sharespersecond, @roundshares, @roundeffort, @created)";

            await con.ExecuteAsync(query, mapped, tx);
        }

        public async Task InsertMinerWorkerPerformanceStatsAsync(IDbConnection con, IDbTransaction tx, MinerWorkerPerformanceStats stats)
        {
            logger.LogInvoke();

            var mapped = mapper.Map<Entities.MinerWorkerPerformanceStats>(stats);

            if (string.IsNullOrEmpty(mapped.Worker))
                mapped.Worker = string.Empty;

            const string query = "INSERT INTO minerstats(poolid, miner, worker, hashrate, sharespersecond, ipaddress, balance, source, created) " +
                "VALUES(@poolid, @miner, @worker, @hashrate, @sharespersecond, @ipaddress, @balance, @source, @created)";

            await con.ExecuteAsync(query, mapped, tx);
        }

        public async Task<PoolStats> GetLastPoolStatsAsync(IDbConnection con, string poolId)
        {
            logger.LogInvoke();

            const string query = "SELECT * FROM poolstats WHERE poolid = @poolId ORDER BY created DESC FETCH NEXT 1 ROWS ONLY";

            var entity = await con.QuerySingleOrDefaultAsync<Entities.PoolStats>(query, new { poolId });
            if (entity == null)
                return null;

            return mapper.Map<PoolStats>(entity);
        }

        public Task<decimal> GetTotalPoolPaymentsAsync(IDbConnection con, string poolId)
        {
            logger.LogInvoke();

            const string query = "SELECT sum(amount) FROM payments WHERE poolid = @poolId";

            return con.ExecuteScalarAsync<decimal>(query, new { poolId });
        }

        public async Task<PoolStats[]> GetPoolPerformanceBetweenAsync(IDbConnection con, string poolId, SampleInterval interval, DateTime start, DateTime end)
        {
            logger.LogInvoke(new object[] { poolId });

            string trunc = null;

            switch (interval)
            {
                case SampleInterval.Hour:
                    trunc = "hour";
                    break;

                case SampleInterval.Day:
                    trunc = "day";
                    break;
            }

            var query = $"SELECT date_trunc('{trunc}', created) AS created, " +
                "AVG(poolhashrate) AS poolhashrate, AVG(networkhashrate) AS networkhashrate, AVG(networkdifficulty) AS networkdifficulty, " +
                "CAST(AVG(connectedminers) AS BIGINT) AS connectedminers, " +
                "CAST(AVG(connectedworkers) AS BIGINT) AS connectedworkers " +
                "FROM poolstats " +
                "WHERE poolid = @poolId AND created >= @start AND created <= @end " +
                $"GROUP BY date_trunc('{trunc}', created) " +
                "ORDER BY created;";

            return (await con.QueryAsync<Entities.PoolStats>(query, new { poolId, start, end }))
                .Select(mapper.Map<PoolStats>)
                .ToArray();
        }

        public async Task<MinerStats> GetMinerStatsAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner)
        {
            logger.LogInvoke(new object[] { poolId, miner });

            var query = "SELECT (SELECT SUM(difficulty) FROM shares WHERE poolid = @poolId AND miner = @miner) AS pendingshares, " +
                "(SELECT amount FROM balances WHERE poolid = @poolId AND address = @miner) AS pendingbalance, " +
                "(SELECT SUM(amount) FROM payments WHERE poolid = @poolId and address = @miner) as totalpaid," +
                "(SELECT SUM(amount) FROM payments WHERE poolid = @poolId and address = @miner and created >= date_trunc('day', now())) as todaypaid";

            var result = await con.QuerySingleOrDefaultAsync<MinerStats>(query, new { poolId, miner }, tx);

            if (result != null)
            {
                query = "SELECT * FROM payments WHERE poolid = @poolId AND address = @miner" +
                    " ORDER BY created DESC LIMIT 1";

                result.LastPayment = await con.QuerySingleOrDefaultAsync<Payment>(query, new { poolId, miner }, tx);

                query = "SELECT created FROM minerstats WHERE poolid = @poolId AND miner = @miner" +
                    " ORDER BY created DESC LIMIT 1";

                var lastUpdate = await con.QuerySingleOrDefaultAsync<DateTime?>(query, new { poolId, miner }, tx);

                if (lastUpdate.HasValue && (clock.Now - DateTime.SpecifyKind(lastUpdate.Value, DateTimeKind.Utc) > MinerStatsMaxAge))
                    lastUpdate = null;

                if (lastUpdate.HasValue)
                {
                    query = "SELECT * FROM minerstats WHERE poolid = @poolId AND miner = @miner AND created = @created";

                    var stats = (await con.QueryAsync<Entities.MinerWorkerPerformanceStats>(query, new { poolId, miner, created = lastUpdate }))
                        .Select(mapper.Map<MinerWorkerPerformanceStats>)
                        .ToArray();

                    if (stats.Any())
                    {
                        foreach (var stat in stats)
                        {
                            if (stat.Worker == null)
                            {
                                stat.Worker = string.Empty;
                                break;
                            }
                        }

                        result.Performance = new WorkerPerformanceStatsContainer
                        {
                            Workers = stats.ToDictionary(x => x.Worker ?? string.Empty, x => new WorkerPerformanceStats
                            {
                                Hashrate = x.Hashrate,
                                SharesPerSecond = x.SharesPerSecond
                            }),

                            Created = stats.First().Created
                        };
                    }
                }
            }

            return result;
        }

        public async Task<MinerWorkerHashrate[]> GetPoolMinerWorkerHashratesAsync(IDbConnection con, string poolId)
        {
            logger.LogInvoke();

            const string query =
                "SELECT s.miner, s.worker, s.hashrate FROM " +
                "(" +
                "    WITH cte AS" +
                "    (" +
                "        SELECT" +
                "            ROW_NUMBER() OVER (partition BY miner, worker ORDER BY created DESC) as rk," +
                "            miner, worker, hashrate" +
                "        FROM minerstats" +
                "        WHERE poolid = @poolId" +
                "    )" +
                "    SELECT miner, worker, hashrate" +
                "    FROM cte" +
                "    WHERE rk = 1" +
                ") s " +
                "WHERE s.hashrate > 0;";

            return (await con.QueryAsync<MinerWorkerHashrate>(query, new { poolId }))
                .ToArray();
        }

        public async Task<WorkerPerformanceStatsContainer[]> GetMinerPerformanceBetweenThreeMinutelyAsync(IDbConnection con, string poolId, string miner, DateTime start, DateTime end)
        {
            logger.LogInvoke(new object[] { poolId });

            const string query = "SELECT date_trunc('hour', created) AS created, " +
                                 "(extract(minute FROM created)::int / 3) AS partition, " +
                                 "worker, AVG(hashrate) AS hashrate, AVG(sharespersecond) AS sharespersecond " +
                                 "FROM minerstats " +
                                 "WHERE poolid = @poolId AND miner = @miner AND created >= @start AND created <= @end " +
                                 "GROUP BY 1, 2, worker " +
                                 "ORDER BY 1, 2, worker";

            var entities = (await con.QueryAsync<Entities.MinerWorkerPerformanceStats>(query, new { poolId, miner, start, end }))
                .ToArray();

            foreach (var entity in entities)
            {
                entity.Worker ??= string.Empty;
                entity.Created = entity.Created.AddMinutes(3 * entity.Partition);
            }

            var entitiesByDate = entities
                .GroupBy(x => x.Created);

            var tmp = entitiesByDate.Select(x => new WorkerPerformanceStatsContainer
            {
                Created = x.Key,
                Workers = x.ToDictionary(y => y.Worker, y => new WorkerPerformanceStats
                {
                    Hashrate = y.Hashrate,
                    SharesPerSecond = y.SharesPerSecond
                })
            })
            .ToArray();

            return tmp;
        }

        public async Task<WorkerPerformanceStatsContainer[]> GetMinerPerformanceBetweenMinutelyAsync(IDbConnection con, string poolId, string miner, DateTime start, DateTime end)
        {
            logger.LogInvoke(new object[] { poolId });

            const string query = "SELECT worker, date_trunc('minute', created) AS created, AVG(hashrate) AS hashrate, " +
                                 "AVG(sharespersecond) AS sharespersecond FROM minerstats " +
                                 "WHERE poolid = @poolId AND miner = @miner AND created >= @start AND created <= @end " +
                                 "GROUP BY date_trunc('minute', created), worker " +
                                 "ORDER BY created, worker;";

            var entities = (await con.QueryAsync<Entities.MinerWorkerPerformanceStats>(query, new { poolId, miner, start, end }))
                .ToArray();

            foreach (var entity in entities)
                entity.Worker ??= string.Empty;

            var entitiesByDate = entities
                .GroupBy(x => x.Created);

            var tmp = entitiesByDate.Select(x => new WorkerPerformanceStatsContainer
            {
                Created = x.Key,
                Workers = x.ToDictionary(y => y.Worker ?? string.Empty, y => new WorkerPerformanceStats
                {
                    Hashrate = y.Hashrate,
                    SharesPerSecond = y.SharesPerSecond
                })
            })
            .ToArray();

            return tmp;
        }

        public async Task<WorkerPerformanceStatsContainer[]> GetMinerPerformanceBetweenHourlyAsync(IDbConnection con, string poolId, string miner, DateTime start, DateTime end)
        {
            logger.LogInvoke(new object[] { poolId });

            const string query = "SELECT worker, date_trunc('hour', created) AS created, AVG(hashrate) AS hashrate, " +
                "AVG(sharespersecond) AS sharespersecond FROM minerstats " +
                "WHERE poolid = @poolId AND miner = @miner AND created >= @start AND created <= @end " +
                "GROUP BY date_trunc('hour', created), worker " +
                "ORDER BY created, worker;";

            var entities = (await con.QueryAsync<Entities.MinerWorkerPerformanceStats>(query, new { poolId, miner, start, end }))
                .ToArray();

            foreach (var entity in entities)
                entity.Worker ??= string.Empty;

            var entitiesByDate = entities
                .GroupBy(x => x.Created);

            var tmp = entitiesByDate.Select(x => new WorkerPerformanceStatsContainer
            {
                Created = x.Key,
                Workers = x.ToDictionary(y => y.Worker ?? string.Empty, y => new WorkerPerformanceStats
                {
                    Hashrate = y.Hashrate,
                    SharesPerSecond = y.SharesPerSecond
                })
            })
            .ToArray();

            return tmp;
        }

        public async Task<WorkerPerformanceStatsContainer[]> GetMinerPerformanceBetweenDailyAsync(IDbConnection con, string poolId, string miner, DateTime start, DateTime end)
        {
            logger.LogInvoke(new object[] { poolId });

            const string query = "SELECT worker, date_trunc('day', created) AS created, AVG(hashrate) AS hashrate, " +
                "AVG(sharespersecond) AS sharespersecond FROM minerstats " +
                "WHERE poolid = @poolId AND miner = @miner AND created >= @start AND created <= @end " +
                "GROUP BY date_trunc('day', created), worker " +
                "ORDER BY created, worker;";

            var entitiesByDate = (await con.QueryAsync<Entities.MinerWorkerPerformanceStats>(query, new { poolId, miner, start, end }))
                .ToArray()
                .GroupBy(x => x.Created);

            var tmp = entitiesByDate.Select(x => new WorkerPerformanceStatsContainer
            {
                Created = x.Key,
                Workers = x.ToDictionary(y => y.Worker, y => new WorkerPerformanceStats
                {
                    Hashrate = y.Hashrate,
                    SharesPerSecond = y.SharesPerSecond
                })
            })
            .ToArray();

            return tmp;
        }

        public async Task<MinerWorkerPerformanceStats[]> PagePoolMinersByHashrateAsync(IDbConnection con, string poolId, DateTime from, int page, int pageSize)
        {
            logger.LogInvoke(new object[] { (object)poolId, from, page, pageSize });

            const string query = "WITH tmp AS " +
                "( " +
                "	SELECT  " +
                "		ms.miner,  " +
                "		ms.hashrate,  " +
                "		ms.sharespersecond,  " +
                "		ms.source,  " +
                "		ROW_NUMBER() OVER(PARTITION BY ms.miner ORDER BY ms.hashrate DESC) AS rk  " +
                "	FROM (SELECT miner, SUM(hashrate) AS hashrate, SUM(sharespersecond) AS sharespersecond, source " +
                "       FROM minerstats " +
                "       WHERE poolid = @poolid AND created >= @from GROUP BY miner, source, created) ms " +
                ") " +
                "SELECT t.miner, t.hashrate, t.sharespersecond, t.source " +
                "FROM tmp t " +
                "WHERE t.rk = 1 " +
                "ORDER by t.hashrate DESC " +
                "OFFSET @offset FETCH NEXT (@pageSize) ROWS ONLY";

            return (await con.QueryAsync<Entities.MinerWorkerPerformanceStats>(query, new { poolId, from, offset = page * pageSize, pageSize }))
                .Select(mapper.Map<MinerWorkerPerformanceStats>)
                .ToArray();
        }

        public async Task<MinerWorkerPerformanceStats[]> AdminPagePoolMinersByHashrateAsync(IDbConnection con, string poolId, DateTime from, int page, int pageSize)
        {
            logger.LogInvoke(new object[] { (object)poolId, from, page, pageSize });

            const string query = "WITH adm AS " +
                "( " +
                "	SELECT  " +
                "		ams.miner,  " +
                "		ams.hashrate,  " +
                "		ams.sharespersecond,  " +
                "		ams.ipaddress,  " +
                "		ams.balance,  " +
                "		ams.source,  " +
                "		ROW_NUMBER() OVER(PARTITION BY ams.miner ORDER BY ams.hashrate DESC) AS ark  " +
                "	FROM (SELECT miner, SUM(hashrate) AS hashrate, SUM(sharespersecond) AS sharespersecond, ipaddress, balance, source " +
                "       FROM minerstats " +
                "       WHERE poolid = @poolid AND created >= @from GROUP BY miner, ipaddress, balance, source, created) ams " +
                ") " +
                "SELECT t.miner, t.hashrate, t.sharespersecond, t.ipaddress, t.balance, t.source " +
                "FROM adm t " +
                "WHERE t.ark = 1 " +
                "ORDER by t.hashrate DESC " +
                "OFFSET @offset FETCH NEXT (@pageSize) ROWS ONLY";

            return (await con.QueryAsync<Entities.MinerWorkerPerformanceStats>(query, new { poolId, from, offset = page * pageSize, pageSize }))
                .Select(mapper.Map<MinerWorkerPerformanceStats>)
                .ToArray();
        }

        public Task<int> DeletePoolStatsBeforeAsync(IDbConnection con, DateTime date)
        {
            logger.LogInvoke();

            const string query = "DELETE FROM poolstats WHERE created < @date";

            return con.ExecuteAsync(query, new { date });
        }

        public Task<int> DeleteMinerStatsBeforeAsync(IDbConnection con, DateTime date)
        {
            logger.LogInvoke();

            const string query = "DELETE FROM minerstats WHERE created < @date";

            return con.ExecuteAsync(query, new { date });
        }
    }
}