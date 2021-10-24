using System;
using System.Data;
using System.Threading.Tasks;
using Cybercore.Persistence.Model;

namespace Cybercore.Persistence.Repositories
{
    public interface IBlockRepository
    {
        Task InsertAsync(IDbConnection con, IDbTransaction tx, Block block);
        Task DeleteBlockAsync(IDbConnection con, IDbTransaction tx, Block block);
        Task UpdateBlockAsync(IDbConnection con, IDbTransaction tx, Block block);
        Task<Block[]> PageBlocksAsync(IDbConnection con, string poolId, BlockStatus[] status, int page, int pageSize);
        Task<Block[]> PageBlocksAsync(IDbConnection con, BlockStatus[] status, int page, int pageSize);
        Task<Block[]> GetPendingBlocksForPoolAsync(IDbConnection con, string poolId);
        Task<Block> GetBlockBeforeAsync(IDbConnection con, string poolId, BlockStatus[] status, DateTime before);
        Task<uint> GetPoolBlockCountAsync(IDbConnection con, string poolId);
        Task<DateTime?> GetLastPoolBlockTimeAsync(IDbConnection con, string poolId);
        Task<double?> GetAverageEffortCountAsync16(IDbConnection con, string poolId);
        Task<double?> GetAverageEffortCountAsync64(IDbConnection con, string poolId);
        Task<double?> GetAverageEffortCountAsync256(IDbConnection con, string poolId);
    }
}