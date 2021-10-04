using System;
using System.Data;
using System.Threading.Tasks;

namespace Cybercore.Persistence.Dummy
{
    public class DummyConnectionFactory : IConnectionFactory
    {
        public DummyConnectionFactory(string connectionString)
        {
        }

        public Task<IDbConnection> OpenConnectionAsync()
        {
            throw new NotImplementedException();
        }
    }
}