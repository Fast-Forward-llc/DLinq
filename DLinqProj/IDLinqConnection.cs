using System.Data;
using System.Linq.Expressions;

namespace DLinq
{
    public interface IDLinqConnection : IDbConnection
    {
        string ConnectionString { get; set; }
        int ConnectionTimeout { get; }
        string Database { get; }
        ConnectionState State { get; }
        int TransactionDepth { get; }

        IDbTransaction BeginTransaction();
        IDbTransaction BeginTransaction(IsolationLevel il);
        void ChangeDatabase(string databaseName);
        void Close();
        void Commit();
        IDbCommand CreateCommand();
        int Delete<T>(Expression<Func<T, bool>> predicate, Options? options = null);
        int Delete<T>(T entity, Options? options = null);
        Task<int> DeleteAsync<T>(Expression<Func<T, bool>> predicate, Options? options = null);
        Task<int> DeleteAsync<T>(T entity, Options? options = null);
        void Dispose();
        T? GetById<T, TKey>(TKey key);
        T? GetById<T>(object keyValues);
        Task<T?> GetByIdAsync<T, TKey>(TKey key);
        Task<T?> GetByIdAsync<T>(object keyValues);
        R? Insert<T, R>(T entity, InsertOptions? options = null);
        T? Insert<T>(T entity, InsertOptions? options = null);
        Task<R?> InsertAsync<T, R>(T entity, InsertOptions? options = null);
        Task<T?> InsertAsync<T>(T entity, InsertOptions? options = null);
        void Open();
        SqlQuery<T> QueryBuilder<T>();
        IEnumerable<T> Query<T>(SqlTextExpression sqlQuery);
        IEnumerable<T> Query<T>(Expression<Func<T, bool>> predicate);
        IEnumerable<T> Query<T>(SqlQuery sqlQuery);
        Task<IEnumerable<T>> QueryAsync<T>(Expression<Func<T, bool>> predicate);
        Task<IEnumerable<T>> QueryAsync<T>(SqlQuery sqlQuery);
        void Rollback();
        SqlQuery<T> From<T>();
        T? Update<T>(T entity, UpdateOptions? options = null);
        Task<T?> UpdateAsync<T>(T entity, UpdateOptions? options = null);
    }
}