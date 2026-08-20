using System.Data;
using System.Linq.Expressions;

namespace DLinq
{
    public interface IDLinqConnection : IDbConnection
    {
        int TransactionDepth { get; }

        void Commit();
        int Delete<T>(Expression<Func<T, bool>> predicate, TableOptions? options = null);
        int Delete<T>(T entity, TableOptions? options = null);
        Task<int> DeleteAsync<T>(Expression<Func<T, bool>> predicate, TableOptions? options = null);
        Task<int> DeleteAsync<T>(T entity, TableOptions? options = null);
        T? GetById<T, TKey>(TKey key, QueryOptions? options = null);
        T? GetById<T>(object keyValues, QueryOptions? options = null);
        Task<T?> GetByIdAsync<T, TKey>(TKey key, QueryOptions? options = null);
        Task<T?> GetByIdAsync<T>(object keyValues, QueryOptions? options = null);
        R? Insert<T, R>(object entity, InsertOptions? options = null);
        T? Insert<T>(T entity, InsertOptions? options = null);
        T? Insert<T>(object entity, InsertOptions? options = null);
        Task<R?> InsertAsync<T, R>(object entity, InsertOptions? options = null);
        Task<T?> InsertAsync<T>(object entity, InsertOptions? options = null);
        Task<T?> InsertAsync<T>(T entity, InsertOptions? options = null);
        SqlQuery<T> QueryBuilder<T>();
        IEnumerable<T> Query<T>(SqlTextExpression sqlQuery);
        IEnumerable<T> Query<T>(Expression<Func<T, bool>> predicate, QueryOptions? options = null);
        IEnumerable<T> Query<T>(SqlQuery sqlQuery, QueryOptions? options = null);
        Task<IEnumerable<T>> QueryAsync<T>(Expression<Func<T, bool>> predicate, QueryOptions? options = null);
        Task<IEnumerable<T>> QueryAsync<T>(SqlQuery sqlQuery, QueryOptions? options = null);
        T QueryFirst<T>(SqlQuery sqlQuery, QueryOptions? options = null);
        Task<T> QueryFirstAsync<T>(SqlQuery sqlQuery, QueryOptions? options = null);
        T QueryFirst<T>(Expression<Func<T, bool>> predicate, QueryOptions? options = null);
        Task<T> QueryFirstAsync<T>(Expression<Func<T, bool>> predicate, QueryOptions? options = null);
        void Rollback();
        SqlQuery<T> From<T>();
        SqlQuery<T> SelectFrom<T>(Expression<Func<T, object>>? selector = null, Expression<Func<T, bool>>? predicate = null);
        T? Update<T>(T entity, UpdateOptions? options = null);
        T? Update<T>(object entity, UpdateOptions? options = null);
        T? Update<T>(object entity, Expression<Func<T, bool>> predicate, UpdateOptions? options = null);
        Task<T?> UpdateAsync<T>(T entity, UpdateOptions? options = null);
        Task<T?> UpdateAsync<T>(object entity, UpdateOptions? options = null);
        Task<T?> UpdateAsync<T>(object entity, Expression<Func<T, bool>> predicate, UpdateOptions? options = null);
        int Exec<T>(string sql, object parameters);
        Task<int> ExecAsync<T>(string sql, object parameters);
    }
}