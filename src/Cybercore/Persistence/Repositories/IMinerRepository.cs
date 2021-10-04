using System.Data;
using System.Threading.Tasks;
using Cybercore.Persistence.Model;

namespace Cybercore.Persistence.Repositories
{
    public interface IMinerRepository
    {
        Task<MinerSettings> GetSettings(IDbConnection con, IDbTransaction tx, string poolId, string address);
        Task UpdateSettings(IDbConnection con, IDbTransaction tx, MinerSettings settings);
    }
}