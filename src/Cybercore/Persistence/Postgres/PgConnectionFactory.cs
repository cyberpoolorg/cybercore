using System.Data;
using System.Threading.Tasks;
using Npgsql;

namespace Cybercore.Persistence.Postgres
{
    public class PgConnectionFactory : IConnectionFactory
    {
        public PgConnectionFactory(string connectionString)
        {
            this.connectionString = connectionString;
        }

        private readonly string connectionString;

        public async Task<IDbConnection> OpenConnectionAsync()
        {
            var con = new NpgsqlConnection(connectionString);
            await con.OpenAsync();
            return con;
        }
    }
}