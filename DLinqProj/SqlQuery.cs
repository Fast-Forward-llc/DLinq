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

        // Optionally, you could add an Alias property here if you want to track it at the query level
        // public string? Alias { get; set; }

        public SqlQuery(QueryProvider provider, Expression expression = null)
        {
            Provider = provider;
            Expression = expression ?? Expression.Constant(this);
        }

        public IEnumerator<T> GetEnumerator() => throw new NotImplementedException();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        // Method to generate Insert SQL for the specified entity
        public (string sql, object parameters) ToInsertSql(T entity, InsertOptions? options = null)
        {
            if (Provider is QueryProvider qp)
            {
                return qp.Translator.GenerateInsertSql(entity, options);
            }
            throw new NotSupportedException("ToInsertSql is only supported for SqlQuery using QueryProvider.");
        }

        public (string sql, object parameters) ToInsertSql<R>(T entity, InsertOptions? options = null)
        {
            if (Provider is QueryProvider qp)
            {
                return qp.Translator.GenerateInsertSql(entity, options);
            }
            throw new NotSupportedException("ToInsertSql is only supported for SqlQuery using QueryProvider.");
        }

        // Method to generate Update SQL for the specified entity
        public (string sql, object parameters) ToUpdateSql(T entity, UpdateOptions? options = null)
        {
            if (Provider is QueryProvider qp)
            {
                return qp.Translator.GenerateUpdateSql(entity, options);
            }
            throw new NotSupportedException("ToUpdateSql is only supported for SqlQuery using QueryProvider.");
        }

        // Method to generate Update SQL for the specified entity with a where predicate
        public (string sql, object parameters) ToUpdateSql(T entity, Expression<Func<T, bool>> wherePredicate, UpdateOptions? options = null)
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
        public SqlQuery<JoinResult<T, TRight>> Join<TRight>(
            SqlQuery<TRight> right,
            Expression<Func<T, TRight, bool>> onPredicate)
        {
            if (right == null) throw new ArgumentNullException(nameof(right));
            if (onPredicate == null) throw new ArgumentNullException(nameof(onPredicate));

            var joinResultType = typeof(JoinResult<,>).MakeGenericType(typeof(T), typeof(TRight));
            var leftParam = Expression.Parameter(typeof(T), "l");
            var rightParam = Expression.Parameter(typeof(TRight), "r");
            var ctor = joinResultType.GetConstructor(new[] { typeof(T), typeof(TRight) });
            var members = new MemberInfo[] {
                joinResultType.GetProperty("Left"),
                joinResultType.GetProperty("Right")
            };
            var newExpr = Expression.New(ctor, new Expression[] { leftParam, rightParam }, members);
            var resultSelector = Expression.Lambda(newExpr, leftParam, rightParam);
            // Compose a custom Join expression node for the translator
            var call = Expression.Call(
                typeof(Queryable),
                "Join", // still use Join for recognizability
                new[] { typeof(T), typeof(TRight), joinResultType },
                this.Expression,
                right.Expression,
                Expression.Quote(onPredicate),
                Expression.Quote(resultSelector)
            );
            return (SqlQuery<JoinResult<T, TRight>>)Provider.CreateQuery(call);
        }
    }
}
