using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DLinq
{
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

        // Method-chaining Join implementation
        public SqlQuery<TResult> Join<TJoin, TKey, TResult>(
            SqlQuery<TJoin> inner,
            Expression<Func<T, TKey>> outerKeySelector,
            Expression<Func<TJoin, TKey>> innerKeySelector,
            Expression<Func<T, TJoin, TResult>> resultSelector)
        {
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            if (outerKeySelector == null) throw new ArgumentNullException(nameof(outerKeySelector));
            if (innerKeySelector == null) throw new ArgumentNullException(nameof(innerKeySelector));
            if (resultSelector == null) throw new ArgumentNullException(nameof(resultSelector));

            // Get the generic method definition from the closed generic type so the declaring type's generic
            // parameter T is already closed. Then construct the concrete generic method for TJoin,TKey,TResult.
            var methodDef = typeof(SqlQuery<T>)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .First(m => m.Name == nameof(Join) && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 3);

            var method = methodDef.MakeGenericMethod(typeof(TJoin), typeof(TKey), typeof(TResult));

            // Call as an instance method on this.Expression
            var call = Expression.Call(
                this.Expression,
                method,
                inner.Expression,
                Expression.Quote(outerKeySelector),
                Expression.Quote(innerKeySelector),
                Expression.Quote(resultSelector)
            );

            return (SqlQuery<TResult>)Provider.CreateQuery<TResult>(call);
        }

        // Overload: Join without explicit inner SqlQuery parameter
        public SqlQuery<TResult> Join<TJoin, TKey, TResult>(
            Expression<Func<T, TKey>> outerKeySelector,
            Expression<Func<TJoin, TKey>> innerKeySelector,
            Expression<Func<T, TJoin, TResult>> resultSelector)
        {
            // Construct the inner SqlQuery<TJoin> using the same provider
            //var inner = new SqlQuery<TJoin>(this.Provider as QueryProvider ?? throw new InvalidOperationException("Provider must be a QueryProvider."));
            if (!(this.Provider is QueryProvider qp)) throw new InvalidOperationException("Provider must be a QueryProvider."); 
            var inner = new SqlQuery<TJoin>(qp); 
            return this.Join(inner, outerKeySelector, innerKeySelector, resultSelector);
        }

        public SqlQuery<T> Where(Expression<Func<T, bool>> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            // Compose the new expression tree
            var call = Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Where),
                new Type[] { typeof(T) },
                this.Expression,
                Expression.Quote(predicate)
            );
            return new SqlQuery<T>((QueryProvider)this.Provider, call);
        }
        public SqlQuery<TResult> Select<TResult>(Expression<Func<T, TResult>> selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));

            var call = Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Select),
                new Type[] { typeof(T), typeof(TResult) },
                this.Expression,
                Expression.Quote(selector)
            );

            return (SqlQuery<TResult>)Provider.CreateQuery<TResult>(call);
        }
    }
}
