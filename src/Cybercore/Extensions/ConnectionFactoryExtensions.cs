using System;
using System.Data;
using System.Threading.Tasks;
using Cybercore.Persistence;

namespace Cybercore.Extensions
{
    public static class ConnectionFactoryExtensions
    {
        public static async Task Run(this IConnectionFactory factory,
            Func<IDbConnection, Task> action)
        {
            using (var con = await factory.OpenConnectionAsync())
            {
                await action(con);
            }
        }

        public static async Task<T> Run<T>(this IConnectionFactory factory,
            Func<IDbConnection, Task<T>> action)
        {
            using (var con = await factory.OpenConnectionAsync())
            {
                return await action(con);
            }
        }

        public static async Task RunTx(this IConnectionFactory factory,
            Func<IDbConnection, IDbTransaction, Task> action,
            bool autoCommit = true, IsolationLevel isolation = IsolationLevel.ReadCommitted)
        {
            using (var con = await factory.OpenConnectionAsync())
            {
                using (var tx = con.BeginTransaction(isolation))
                {
                    try
                    {
                        await action(con, tx);

                        if (autoCommit)
                            tx.Commit();
                    }

                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }

        public static async Task<T> RunTx<T>(this IConnectionFactory factory,
            Func<IDbConnection, IDbTransaction, Task<T>> func,
            bool autoCommit = true, IsolationLevel isolation = IsolationLevel.ReadCommitted)
        {
            using (var con = await factory.OpenConnectionAsync())
            {
                using (var tx = con.BeginTransaction(isolation))
                {
                    try
                    {
                        var result = await func(con, tx);

                        if (autoCommit)
                            tx.Commit();

                        return result;
                    }

                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }
    }
}