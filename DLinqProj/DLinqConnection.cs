using System;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Npgsql;
using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using Dapper;
using System.Diagnostics;
using System.Text.Json;

namespace DLinq
{
    public class DLinqConnection : IDisposable, IDLinqConnection
    {
        private IDbConnection? _conn;
        private readonly QueryProvider _provider;
        private readonly IDapperProvider _dapper;
        private IDbTransaction? _transaction;
        private int _transactionDepth = 0;
        private bool _recursiveCommit;
        private bool _recursiveRollback;
        private bool _recursiveDispose;
        private bool _wasClosed;

        public DLinqConnection(IDbConnection connection, ISqlDialect dialect, IDapperProvider? dapperProvider = null)
        {
            _conn = connection;
            _provider = new QueryProvider(dialect);
            _dapper = dapperProvider ?? new DapperProvider(connection);
        }

        public virtual IEnumerable<T> Query<T>(SqlTextExpression sqlQuery)
        {
#if DEBUG_SQL
            Console.WriteLine($"Executing query: {sqlQuery.Sql} \r\nWith Parameters: {JsonSerializer.Serialize(sqlQuery.Parameters)}");
#endif
            return _dapper.Query<T>(sqlQuery.Sql, sqlQuery.Parameters, GetCurrentTransaction()!);
        }

        public virtual IEnumerable<T> Query<T>(SqlQuery sqlQuery)
        {
            var (sql, parameters) = sqlQuery.ToSql();
#if DEBUG_SQL
            Console.WriteLine($"Executing query: {sql} \r\nWith Parameters: {JsonSerializer.Serialize(parameters)}");
#endif
            return _dapper.Query<T>(sql, parameters, GetCurrentTransaction()!);
        }

        public virtual IEnumerable<T> Query<T>(Expression<Func<T, bool>> predicate)
        {
            var query = From<T>().Where(predicate);
            var (sql, parameters) = query.ToSql();
            #if DEBUG_SQL
                        Console.WriteLine($"Executing query: {sql}");
            #endif
            return _dapper.Query<T>(sql, parameters, GetCurrentTransaction()!);
        }

        public virtual async Task<IEnumerable<T>> QueryAsync<T>(SqlQuery sqlQuery)
        {
            var (sql, parameters) = sqlQuery.ToSql();
            #if DEBUG_SQL
                        Console.WriteLine($"Executing query: {sql}");
            #endif
            return await _dapper.QueryAsync<T>(sql, parameters, GetCurrentTransaction()!);
        }

        public virtual async Task<IEnumerable<T>> QueryAsync<T>(Expression<Func<T, bool>> predicate)
        {
            var query = From<T>().Where(predicate);
            var (sql, parameters) = query.ToSql();
            #if DEBUG_SQL
                        Console.WriteLine($"Executing query: {sql}");
            #endif
            return await _dapper.QueryAsync<T>(sql, parameters, GetCurrentTransaction()!);
        }

        // Expose SqlQuery<T> for LINQ operations
        public virtual SqlQuery<T> From<T>() => new SqlQuery<T>(_provider);
        public virtual SqlQuery<T> QueryBuilder<T>() => new SqlQuery<T>(_provider);

        public virtual int TransactionDepth => _transactionDepth;

        // Helper to pass transaction to DapperProvider
        private IDbTransaction? GetCurrentTransaction()
        {
            if (_transaction is DLinq.Transaction dlinqTrans)
            {
                return dlinqTrans.InnerTransaction;
            }
            return _transaction;
        }

        /// <summary>
        /// Gets an entity of type T by its key(s).
        /// Pass an object whose properties match the key fields of T.
        /// </summary>
        public virtual T? GetById<T>(object keyValues)
        {
            var (sql, parameters) = GetByIdCore<T>(keyValues);
            #if DEBUG_SQL
                        Console.WriteLine($"Executing query: {sql}");
            #endif
            Open();
            try
            {
                return _dapper.QuerySingleOrDefault<T>(sql, parameters!, GetCurrentTransaction()!);
            }
            finally
            {
                _Close();
            }
        }

        /// <summary>
        /// Gets an entity of type T by its key(s).
        /// Pass an object whose properties match the key fields of T.
        /// </summary>
        public virtual async Task<T?> GetByIdAsync<T>(object keyValues)
        {
            var (sql, parameters) = GetByIdCore<T>(keyValues);
            #if DEBUG_SQL
                        Console.WriteLine($"Executing query: {sql}");
            #endif
            Open();
            try
            {
                return await _dapper.QuerySingleOrDefaultAsync<T>(sql, parameters!, GetCurrentTransaction()!);
            }
            finally
            {
                _Close();
            }
        }

        protected virtual (string sql, object? parameters) GetByIdCore<T>(object keyValues)
        {
            var keyProps = typeof(T).GetProperties()
                .Where(p => p.GetCustomAttributes(typeof(KeyAttribute), true).Any())
                .ToArray();
            if (keyProps.Length == 0)
                throw new InvalidOperationException($"Type {typeof(T).Name} does not have any [Key] properties.");
            if (keyValues == null)
                throw new ArgumentNullException(nameof(keyValues));

            var keyValueType = keyValues.GetType();
            var values = new object[keyProps.Length];
            for (int i = 0; i < keyProps.Length; i++)
            {
                var keyProp = keyProps[i];
                var valueProp = keyValueType.GetProperty(keyProp.Name);
                if (valueProp == null)
                    throw new ArgumentException($"Key value object does not contain property '{keyProp.Name}'.");
                values[i] = valueProp.GetValue(keyValues)!;
            }

            var param = Expression.Parameter(typeof(T), "x");
            Expression predicate = null;
            for (int i = 0; i < keyProps.Length; i++)
            {
                var member = Expression.Property(param, keyProps[i]);
                var constant = Expression.Constant(values[i], keyProps[i].PropertyType);
                var equal = Expression.Equal(member, constant);
                predicate = predicate == null ? equal : Expression.AndAlso(predicate, equal);
            }
            var lambda = Expression.Lambda<Func<T, bool>>(predicate, param);
            var query = From<T>().Where(lambda);
            var (sql, parameters) = query.ToSql();
            return (sql, parameters);
        }

        /// <summary>
        /// Gets an entity of type T by its single key field.
        /// </summary>
        public virtual T? GetById<T, TKey>(TKey key)
        {
            var (sql, parameters) = GetByIdCore<T, TKey>(key);
            #if DEBUG_SQL
                        Console.WriteLine($"Executing query: {sql}");
            #endif
            Open();
            try
            {
                return _dapper.QuerySingleOrDefault<T>(sql, parameters, GetCurrentTransaction()!);
            }
            finally
            {
                _Close();
            }
        }

        /// <summary>
        /// Gets an entity of type T by its single key field.
        /// </summary>
        public virtual async Task<T?> GetByIdAsync<T, TKey>(TKey key)
        {
            var (sql, parameters) = GetByIdCore<T, TKey>(key);
            #if DEBUG_SQL
                        Console.WriteLine($"Executing query: {sql}");
            #endif
            Open();
            try
            {
                return await _dapper.QuerySingleOrDefaultAsync<T>(sql, parameters, GetCurrentTransaction()!);
            }
            finally
            {
                _Close();
            }
        }

        protected virtual (string sql, object? parameters) GetByIdCore<T, TKey>(TKey key)
        {
            var keyProps = typeof(T).GetProperties()
                .Where(p => p.GetCustomAttributes(typeof(KeyAttribute), true).Any())
                .ToArray();
            if (keyProps.Length == 0)
                throw new InvalidOperationException($"Type {typeof(T).Name} does not have any [Key] properties.");
            if (keyProps.Length > 1)
                throw new InvalidOperationException($"Type {typeof(T).Name} has multiple [Key] properties. Use the object overload for composite keys.");

            var param = Expression.Parameter(typeof(T), "x");
            var member = Expression.Property(param, keyProps[0]);
            var constant = Expression.Constant(key, keyProps[0].PropertyType);
            var equal = Expression.Equal(member, constant);
            var lambda = Expression.Lambda<Func<T, bool>>(equal, param);
            var query = From<T>().Where(lambda);
            var (sql, parameters) = query.ToSql();
            return (sql, parameters);
        }

        /// <summary>
        /// Inserts an entity of type T into the database. If Option.SelectAfterMutation is true, returns the inserted entity.
        /// </summary>
        public virtual T? Insert<T>(T entity, InsertOptions? options = null)
        {
            Open();
            try
            {
                var (sql, parameters) = From<T>().ToInsertSql(entity, options);
                #if DEBUG_SQL
                            Console.WriteLine($"Executing query: {sql}");
                #endif
                if (options?.SelectAfterMutation == true)
                {
                    return _dapper.QuerySingleOrDefault<T>(sql, parameters, GetCurrentTransaction()!);
                }
                _dapper.Execute(sql, parameters, GetCurrentTransaction()!);
                return default;
            }
            finally
            {
                _Close();
            }
        }

        /// <summary>
        /// Inserts an entity of type T into the database. If Option.SelectAfterMutation is true, returns the inserted entity.
        /// </summary>
        public virtual async Task<T?> InsertAsync<T>(T entity, InsertOptions? options = null)
        {
            Open();
            try
            {
                var (sql, parameters) = From<T>().ToInsertSql(entity, options);
                #if DEBUG_SQL
                            Console.WriteLine($"Executing query: {sql}");
                #endif
                if (options?.SelectAfterMutation == true)
                {
                    return await _dapper.QuerySingleOrDefaultAsync<T>(sql, parameters, GetCurrentTransaction()!);
                }
                await _dapper.ExecuteAsync(sql, parameters, GetCurrentTransaction()!);
                return default;
            }
            finally
            {
                _Close();
            }
        }

        /// <summary>
        /// Inserts an entity of type T into the database. If Option.SelectAfterMutation is true, returns the inserted entity.
        /// </summary>
        public virtual R? Insert<T, R>(T entity, InsertOptions? options = null)
        {
            Open();
            try
            {
                var (sql, parameters) = From<T>().ToInsertSql<R>(entity, options);
                #if DEBUG_SQL
                            Console.WriteLine($"Executing query: {sql}");
                #endif
                if (options?.SelectAfterMutation == true)
                {
                    return _dapper.QuerySingleOrDefault<R>(sql, parameters, GetCurrentTransaction()!);
                }
                _dapper.Execute(sql, parameters, GetCurrentTransaction()!);
                return default;
            }
            finally
            {
                _Close();
            }
        }
        /// <summary>
        /// Inserts an entity of type T into the database. If Option.SelectAfterMutation is true, returns the inserted entity.
        /// </summary>
        public virtual async Task<R?> InsertAsync<T, R>(T entity, InsertOptions? options = null)
        {
            Open();
            try
            {
                var (sql, parameters) = From<T>().ToInsertSql<R>(entity, options);
                #if DEBUG_SQL
                            Console.WriteLine($"Executing query: {sql}");
                #endif
                if (options?.SelectAfterMutation == true)
                {
                    return await _dapper.QuerySingleOrDefaultAsync<R>(sql, parameters, GetCurrentTransaction()!);
                }
                await _dapper.ExecuteAsync(sql, parameters, GetCurrentTransaction()!);
                return default;
            }
            finally
            {
                _Close();
            }
        }

        /// <summary>
        /// Updates an entity of type T in the database. If Option.SelectAfterMutation is true, returns the updated entity.
        /// </summary>
        public virtual T? Update<T>(T entity, UpdateOptions? options = null)
        {
            Open();
            try
            {
                var (sql, parameters) = From<T>().ToUpdateSql(entity, options);
                #if DEBUG_SQL
                            Console.WriteLine($"Executing query: {sql}");
                #endif
                if (options?.SelectAfterMutation == true)
                {
                    return _dapper.QuerySingleOrDefault<T>(sql, parameters, GetCurrentTransaction()!);
                }
                _dapper.Execute(sql, parameters, GetCurrentTransaction()!);
                return default;
            }
            finally
            {
                _Close();
            }
        }

        public virtual async Task<T?> UpdateAsync<T>(T entity, UpdateOptions? options = null)
        {
            Open();
            try
            {
                var (sql, parameters) = From<T>().ToUpdateSql(entity, options);
                #if DEBUG_SQL
                            Console.WriteLine($"Executing query: {sql}");
                #endif
                if (options?.SelectAfterMutation == true)
                {
                    return await _dapper.QuerySingleOrDefaultAsync<T>(sql, parameters, GetCurrentTransaction()!);
                }
                await _dapper.ExecuteAsync(sql, parameters, GetCurrentTransaction()!);
                return default;
            }
            finally
            {
                _Close();
            }
        }

        /// <summary>
        /// Deletes entities of type T from the database matching the given predicate.
        /// </summary>
        public virtual int Delete<T>(Expression<Func<T, bool>> predicate, Options? options = null)
        {
            Open();
            try
            {
                // Use SqlQuery<T>.ToDeleteSql to generate SQL and parameters from the predicate
                var query = From<T>();
                var (sql, parameters) = query.ToDeleteSql(predicate, options);
                #if DEBUG_SQL
                            Console.WriteLine($"Executing query: {sql}\r\n{JsonSerializer.Serialize(parameters)}");
                #endif
                return _dapper.Execute(sql, parameters, GetCurrentTransaction()!);
            }
            finally
            {
                _Close();
            }
        }

        /// <summary>
        /// Deletes entities of type T from the database matching the given predicate.
        /// </summary>
        public virtual async Task<int> DeleteAsync<T>(Expression<Func<T, bool>> predicate, Options? options = null)
        {
            Open();
            try
            {
                // Use SqlQuery<T>.ToDeleteSql to generate SQL and parameters from the predicate
                var query = From<T>();
                var (sql, parameters) = query.ToDeleteSql(predicate, options);
                #if DEBUG_SQL
                            Console.WriteLine($"Executing query: {sql}");
                #endif
                return await _dapper.ExecuteAsync(sql, parameters, GetCurrentTransaction()!);
            }
            finally
            {
                _Close();
            }
        }

        /// <summary>
        /// Deletes entities of type T from the database matching the given predicate.
        /// </summary>
        public virtual int Delete<T>(T entity, Options? options = null)
        {
            Open();
            try
            {
                var (sql, parameters) = From<T>().ToDeleteSql(entity, options);
                #if DEBUG_SQL
                            Console.WriteLine($"Executing query: {sql}");
                #endif
                return _dapper.Execute(sql, parameters, GetCurrentTransaction()!);
            }
            finally
            {
                _Close();
            }
        }

        /// <summary>
        /// Deletes entities of type T from the database matching the given predicate.
        /// </summary>
        public virtual async Task<int> DeleteAsync<T>(T entity, Options? options = null)
        {
            Open();
            try
            {
                var (sql, parameters) = From<T>().ToDeleteSql(entity, options);
                #if DEBUG_SQL
                            Console.WriteLine($"Executing query: {sql}");
                #endif
                return await _dapper.ExecuteAsync(sql, parameters, GetCurrentTransaction()!);
            }
            finally
            {
                _Close();
            }
        }

        /// <summary>
        /// Commits the current transaction if one exists, and decrements transaction depth.
        /// Committing a null transaction has no effect.
        /// </summary>
        public virtual void Commit()
        {
            if (_recursiveCommit) return; //protects from possible recursion from Transaction.Commit calling back
            _recursiveCommit = true;
            if (_transaction != null)
            {
                _transaction.Commit();
                _transaction = null;
                if (_transactionDepth > 0) _transactionDepth--;
            }
            _recursiveCommit = false;
        }

        /// <summary>
        /// Rolls back the current transaction and resets transaction depth.
        /// Rolling back a null transaction throws InvalidOperationException.
        /// </summary>
        public virtual void Rollback()
        {
            if (_recursiveRollback) return; //protects from possible recursion from Transaction.Rollback calling back
            _recursiveRollback = true;
            _transaction!.Rollback();
            _transaction = null;
            _transactionDepth = 0;
            _recursiveRollback = false;
        }

        // IDbConnection implementation
        public virtual string ConnectionString { get { return _conn?.ConnectionString!; } set { if (_conn != null) _conn.ConnectionString = value; } }
        public virtual int ConnectionTimeout => _conn?.ConnectionTimeout ?? 0;
        public virtual string Database => _conn?.Database!;
        public virtual ConnectionState State => _conn?.State ?? ConnectionState.Closed;
        public virtual void ChangeDatabase(string databaseName) => _conn?.ChangeDatabase(databaseName);
        public virtual void Close()
        {
            if (_conn == null) return;
            _conn.Close();
        }
        private void _Close()
        {
            if (_wasClosed) Close();
        }
        public virtual IDbCommand CreateCommand() => _conn.CreateCommand();
        public virtual void Open()
        {
            if (_conn == null) throw new InvalidOperationException("Cannot open a null connection. It may have been disposed.");
            if (_conn.State == ConnectionState.Broken) Close();
            if (_conn.State == ConnectionState.Closed) { _conn.Open(); _wasClosed = true; }
            else _wasClosed = false;
        }

        /// <summary>
        /// Begins a transaction. Opens the connection if it is not already open. 
        /// If The connection is opened automaticly by BeginTransaction it is also closed when the transaction is disposed or goes out of scope.
        /// Supports nested transactions by counting depth.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public virtual IDbTransaction BeginTransaction()
        {
            Open();
            if (_conn == null) throw new InvalidOperationException("Cannot begin transaction of a null connection. It may have been disposed.");
            if (_transaction == null) _transaction = new Transaction(_conn.BeginTransaction(), Commit, Rollback, TransDispose);
            _transactionDepth++;
            return _transaction;
        }

        //Begins a transaction with specified isolation level. Supports nested transactions by counting depth.
        public virtual IDbTransaction BeginTransaction(IsolationLevel il)
        {
            Open();
            if (_conn == null) throw new InvalidOperationException("Cannot begin transaction of a null connection. It may have been disposed.");
            if (_transaction == null) _transaction = new Transaction(_conn.BeginTransaction(il), Commit, Rollback, TransDispose);
            _transactionDepth++;
            return _transaction;
        }

        private void TransDispose()
        {
            if (_recursiveDispose) return; //protects from possible recursion from Transaction.Dispose calling back
            _recursiveDispose = true;
            _transaction?.Dispose();
            _transaction = null;
            if (_wasClosed) _Close();
            _recursiveDispose = false;
        }

        public virtual void Dispose()
        {
            TransDispose();
            Close();
            _conn?.Dispose();
            _conn = null!;
        }
    }
}
