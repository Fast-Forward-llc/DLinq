using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using static Dapper.SqlMapper;

namespace DLinq
{
    public class QueryProvider : IQueryProvider
    {
        private readonly ISqlDialect _dialect;
        private readonly QueryTranslator _translator;

        public QueryProvider(ISqlDialect dialect)
        {
            _dialect = dialect;
            _translator = new QueryTranslator(_dialect);
        }

        public QueryTranslator Translator => _translator;

        public IQueryable CreateQuery(Expression expression)
        {
            var elementType = expression.Type.GetGenericArguments().First();
            var queryType = typeof(SqlQuery<>).MakeGenericType(elementType);
            return (IQueryable)Activator.CreateInstance(queryType, this, expression);
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            return new SqlQuery<TElement>(this, expression);
        }

        public object Execute(Expression expression)
        {
            var sql = _translator.Translate(expression, out var parameters);
            return sql;
        }

        public TResult Execute<TResult>(Expression expression)
        {
            return (TResult)Execute(expression);
        }

        // Alias for Execute: ToSql
        public object ToSql(Expression expression)
        {
            return Execute(expression);
        }

        public TResult ToSql<TResult>(Expression expression)
        {
            return Execute<TResult>(expression);
        }
    }

    public class SqlQuery<T> : IOrderedQueryable<T>
    {
        public Expression Expression { get; }
        public Type ElementType => typeof(T);
        public IQueryProvider Provider { get; }

        public SqlQuery(QueryProvider provider, Expression expression = null)
        {
            Provider = provider;
            Expression = expression ?? Expression.Constant(this);
        }

        public IEnumerator<T> GetEnumerator() => throw new NotImplementedException();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        // Method to generate Insert SQL for the specified entity
        public (string sql, object parameters) ToInsertSql(T entity, Options? options = null)
        {
            if (Provider is QueryProvider qp)
            {
                return qp.Translator.GenerateInsertSql(entity, options);
            }
            throw new NotSupportedException("ToInsertSql is only supported for SqlQuery using QueryProvider.");
        }

        public (string sql, object parameters) ToInsertSql<R>(T entity, Options? options = null)
        {
            if (Provider is QueryProvider qp)
            {
                return qp.Translator.GenerateInsertSql(entity, options);
            }
            throw new NotSupportedException("ToInsertSql is only supported for SqlQuery using QueryProvider.");
        }

        // Method to generate Update SQL for the specified entity
        public (string sql, object parameters) ToUpdateSql(T entity, Options? options = null)
        {
            if (Provider is QueryProvider qp)
            {
                return qp.Translator.GenerateUpdateSql(entity, options);
            }
            throw new NotSupportedException("ToUpdateSql is only supported for SqlQuery using QueryProvider.");
        }

        // Method to generate Update SQL for the specified entity with a where predicate
        public (string sql, object parameters) ToUpdateSql(T entity, Expression<Func<T, bool>> wherePredicate, Options? options = null)
        {
            if (Provider is QueryProvider qp)
            {
                return qp.Translator.GenerateUpdateSql(entity, wherePredicate?.Body, options);
            }
            throw new NotSupportedException("ToUpdateSql is only supported for SqlQuery using QueryProvider.");
        }

        // Existing overload for backward compatibility
        public (string sql, object parameters) ToUpdateSql(T entity)
        {
            if (Provider is QueryProvider qp)
            {
                return qp.Translator.GenerateUpdateSql(entity);
            }
            throw new NotSupportedException("ToUpdateSql is only supported for SqlQuery using QueryProvider.");
        }

        // Method to generate Delete SQL for the specified entity type with a where predicate
        public (string sql, object parameters) ToDeleteSql(Expression<Func<T, bool>> wherePredicate, Options? options = null)
        {
            if (Provider is QueryProvider qp)
            {
                return qp.Translator.GenerateDeleteSql(typeof(T), wherePredicate?.Body, options);
            }
            throw new NotSupportedException("ToDeleteSql is only supported for SqlQuery using QueryProvider.");
        }

        // Overload to generate Delete SQL for an entity instance by its key fields
        public (string sql, object parameters) ToDeleteSql(T entity, Options? options = null)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
            if (Provider is QueryProvider qp)
            {
                if (qp.Translator == null)
                    throw new InvalidOperationException("QueryTranslator is not available.");
                var entityType = typeof(T);
                var keyProps = entityType.GetProperties()
                    .Where(p => p.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.KeyAttribute), true).Any())
                    .ToArray();
                if (keyProps.Length == 0)
                    throw new InvalidOperationException($"Type {entityType.Name} does not have any [Key] properties.");
                var keyValues = new Dictionary<string, object>();
                foreach (var prop in keyProps)
                {
                    var colAttr = prop.GetCustomAttribute(typeof(System.ComponentModel.DataAnnotations.Schema.ColumnAttribute)) as System.ComponentModel.DataAnnotations.Schema.ColumnAttribute;
                    var colName = colAttr?.Name ?? prop.Name;
                    keyValues[colName] = prop.GetValue(entity);
                }
                // Use GenerateDeleteSql with keyValues
                return qp.Translator.GenerateDeleteSql(entityType, null, options, keyValues);
            }
            throw new NotSupportedException("ToDeleteSql is only supported for SqlQuery using QueryProvider.");
        }
    }
}