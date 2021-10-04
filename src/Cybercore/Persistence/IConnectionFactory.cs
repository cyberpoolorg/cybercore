using System.Data;
using System.Threading.Tasks;

namespace Cybercore.Persistence
{
    public interface IConnectionFactory
    {
        Task<IDbConnection> OpenConnectionAsync();
    }
}