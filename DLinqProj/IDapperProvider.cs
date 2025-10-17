using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace DLinq
{
    public interface IDapperProvider
    {
        T? QuerySingleOrDefault<T>(string sql, object param = null, IDbTransaction transaction = null);
        IEnumerable<T> Query<T>(string sql, object param = null, IDbTransaction transaction = null);
        T? QueryFirstOrDefault<T>(string sql, object param = null, IDbTransaction transaction = null);
        T QuerySingle<T>(string sql, object param = null, IDbTransaction transaction = null);
        int Execute(string sql, object param = null, IDbTransaction transaction = null);
        IEnumerable<dynamic> Query(string sql, object param = null, IDbTransaction transaction = null);
        Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object param = null, IDbTransaction transaction = null);
        Task<IEnumerable<T>> QueryAsync<T>(string sql, object param = null, IDbTransaction transaction = null);
        Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object param = null, IDbTransaction transaction = null);
        Task<T> QuerySingleAsync<T>(string sql, object param = null, IDbTransaction transaction = null);
        Task<int> ExecuteAsync(string sql, object param = null, IDbTransaction transaction = null);
        Task<IEnumerable<dynamic>> QueryAsync(string sql, object param = null, IDbTransaction transaction = null);
    }
}
