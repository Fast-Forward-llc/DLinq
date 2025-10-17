using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace DLinq
{
    /// <summary>
    /// Wrapper for Dapper extension methods to allow mocking in unit tests.
    /// </summary>
    public class DapperProvider : IDapperProvider
    {
        public IDbConnection Connection { get; protected set; }

        public DapperProvider(IDbConnection connection)
        {
            Connection = connection;
        }

        public virtual T? QuerySingleOrDefault<T>(string sql, object param = null, IDbTransaction transaction = null)
        {
            return Dapper.SqlMapper.QuerySingleOrDefault<T>(Connection, sql, param, transaction);
        }

        public virtual IEnumerable<T> Query<T>(string sql, object param = null, IDbTransaction transaction = null)
        {
            return Dapper.SqlMapper.Query<T>(Connection, sql, param, transaction);
        }

        public virtual T? QueryFirstOrDefault<T>(string sql, object param = null, IDbTransaction transaction = null)
        {
            return Dapper.SqlMapper.QueryFirstOrDefault<T>(Connection, sql, param, transaction);
        }

        public virtual T QuerySingle<T>(string sql, object param = null, IDbTransaction transaction = null)
        {
            return Dapper.SqlMapper.QuerySingle<T>(Connection, sql, param, transaction);
        }

        public virtual int Execute(string sql, object param = null, IDbTransaction transaction = null)
        {
            return Dapper.SqlMapper.Execute(Connection, sql, param, transaction);
        }

        public virtual IEnumerable<dynamic> Query(string sql, object param = null, IDbTransaction transaction = null)
        {
            return Dapper.SqlMapper.Query(Connection, sql, param, transaction);
        }

        // Async versions
        public virtual Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object param = null, IDbTransaction transaction = null)
        {
            return Dapper.SqlMapper.QuerySingleOrDefaultAsync<T>(Connection, sql, param, transaction);
        }

        public virtual Task<IEnumerable<T>> QueryAsync<T>(string sql, object param = null, IDbTransaction transaction = null)
        {
            return Dapper.SqlMapper.QueryAsync<T>(Connection, sql, param, transaction);
        }

        public virtual Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object param = null, IDbTransaction transaction = null)
        {
            return Dapper.SqlMapper.QueryFirstOrDefaultAsync<T>(Connection, sql, param, transaction);
        }

        public virtual Task<T> QuerySingleAsync<T>(string sql, object param = null, IDbTransaction transaction = null)
        {
            return Dapper.SqlMapper.QuerySingleAsync<T>(Connection, sql, param, transaction);
        }

        public virtual Task<int> ExecuteAsync(string sql, object param = null, IDbTransaction transaction = null)
        {
            return Dapper.SqlMapper.ExecuteAsync(Connection, sql, param, transaction);
        }

        public virtual Task<IEnumerable<dynamic>> QueryAsync(string sql, object param = null, IDbTransaction transaction = null)
        {
            return Dapper.SqlMapper.QueryAsync(Connection, sql, param, transaction);
        }
    }
}
