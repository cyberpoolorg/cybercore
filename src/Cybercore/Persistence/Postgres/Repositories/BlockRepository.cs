using AutoMapper;
using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Cybercore.Extensions;
using Cybercore.Persistence.Model;
using Cybercore.Persistence.Repositories;
using Cybercore.Util;
using Dapper;
using NLog;

namespace Cybercore.Persistence.Postgres.Repositories
{
    public class BlockRepository : IBlockRepository
    {
        public BlockRepository(IMapper mapper)
        {
            this.mapper = mapper;
        }

        private readonly IMapper mapper;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        public async Task InsertAsync(IDbConnection con, IDbTransaction tx, Block block)
        {
            logger.LogInvoke();

            var mapped = mapper.Map<Entities.Block>(block);

            const string query =
                "INSERT INTO blocks(poolid, blockheight, networkdifficulty, status, type, transactionconfirmationdata, miner, reward, effort, confirmationprogress, source, hash, created) " +
                "VALUES(@poolid, @blockheight, @networkdifficulty, @status, @type, @transactionconfirmationdata, @miner, @reward, @effort, @confirmationprogress, @source, @hash, @created)";

            await con.ExecuteAsync(query, mapped, tx);
        }

        public async Task DeleteBlockAsync(IDbConnection con, IDbTransaction tx, Block block)
        {
            logger.LogInvoke();

            const string query = "DELETE FROM blocks WHERE id = @id";
            await con.ExecuteAsync(query, block, tx);
        }

        public async Task UpdateBlockAsync(IDbConnection con, IDbTransaction tx, Block block)
        {
            logger.LogInvoke();

            var mapped = mapper.Map<Entities.Block>(block);

            const string query = "UPDATE blocks SET blockheight = @blockheight, status = @status, type = @type, reward = @reward, effort = @effort, confirmationprogress = @confirmationprogress, hash = @hash WHERE id = @id";
            await con.ExecuteAsync(query, mapped, tx);
        }

        public async Task<Block[]> PageBlocksAsync(IDbConnection con, string poolId, BlockStatus[] status, int page, int pageSize)
        {
            logger.LogInvoke(new object[] { poolId });

            const string query = "SELECT * FROM blocks WHERE poolid = @poolid AND status = ANY(@status) " +
                "ORDER BY created DESC OFFSET @offset FETCH NEXT (@pageSize) ROWS ONLY";

            return (await con.QueryAsync<Entities.Block>(query, new
            {
                poolId,
                status = status.Select(x => x.ToString().ToLower()).ToArray(),
                offset = page * pageSize,
                pageSize
            }))
            .Select(mapper.Map<Block>)
            .ToArray();
        }

        public async Task<Block[]> PageBlocksAsync(IDbConnection con, BlockStatus[] status, int page, int pageSize)
        {
            const string query = "SELECT * FROM blocks WHERE status = ANY(@status) " +
                "ORDER BY created DESC OFFSET @offset FETCH NEXT (@pageSize) ROWS ONLY";

            return (await con.QueryAsync<Entities.Block>(query, new
            {
                status = status.Select(x => x.ToString().ToLower()).ToArray(),
                offset = page * pageSize,
                pageSize
            }))
            .Select(mapper.Map<Block>)
            .ToArray();
        }

        public async Task<Block[]> GetPendingBlocksForPoolAsync(IDbConnection con, string poolId)
        {
            logger.LogInvoke(new object[] { poolId });

            const string query = "SELECT * FROM blocks WHERE poolid = @poolid AND status = @status";

            return (await con.QueryAsync<Entities.Block>(query, new { status = BlockStatus.Pending.ToString().ToLower(), poolid = poolId }))
                .Select(mapper.Map<Block>)
                .ToArray();
        }

        public async Task<Block> GetBlockBeforeAsync(IDbConnection con, string poolId, BlockStatus[] status, DateTime before)
        {
            logger.LogInvoke(new object[] { poolId });

            const string query = "SELECT * FROM blocks WHERE poolid = @poolid AND status = ANY(@status) AND created < @before " +
                "ORDER BY created DESC FETCH NEXT (1) ROWS ONLY";

            return (await con.QueryAsync<Entities.Block>(query, new
            {
                poolId,
                before,
                status = status.Select(x => x.ToString().ToLower()).ToArray()
            }))
            .Select(mapper.Map<Block>)
            .FirstOrDefault();
        }

        public Task<uint> GetPoolBlockCountAsync(IDbConnection con, string poolId)
        {
            logger.LogInvoke(new object[] { poolId });

            const string query = "SELECT COUNT(*) FROM blocks WHERE poolid = @poolId";

            return con.ExecuteScalarAsync<uint>(query, new { poolId });
        }

        public Task<DateTime?> GetLastPoolBlockTimeAsync(IDbConnection con, string poolId)
        {
            logger.LogInvoke(new object[] { poolId });

            const string query = "SELECT created FROM blocks WHERE poolid = @poolId ORDER BY created DESC LIMIT 1";

            return con.ExecuteScalarAsync<DateTime?>(query, new { poolId });
        }

        public Task<double?> GetAverageEffortCountAsync16(IDbConnection con, string poolId)
        {
            logger.LogInvoke(new object[] { poolId });

            const string query = "WITH eff16 AS (SELECT blockheight , effort FROM blocks WHERE poolid = @poolId ORDER BY blockheight DESC LIMIT 16) SELECT avg(effort) FROM eff16";

            return con.ExecuteScalarAsync<double?>(query, new { poolId });
        }

        public Task<double?> GetAverageEffortCountAsync64(IDbConnection con, string poolId)
        {
            logger.LogInvoke(new object[] { poolId });

            const string query = "WITH eff64 AS (SELECT blockheight , effort FROM blocks WHERE poolid = @poolId ORDER BY blockheight DESC LIMIT 64) SELECT avg(effort) FROM eff64";

            return con.ExecuteScalarAsync<double?>(query, new { poolId });
        }

        public Task<double?> GetAverageEffortCountAsync256(IDbConnection con, string poolId)
        {
            logger.LogInvoke(new object[] { poolId });

            const string query = "WITH eff256 AS (SELECT blockheight , effort FROM blocks WHERE poolid = @poolId ORDER BY blockheight DESC LIMIT 256) SELECT avg(effort) FROM eff256";

            return con.ExecuteScalarAsync<double?>(query, new { poolId });
        }
    }
}