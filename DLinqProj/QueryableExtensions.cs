using System.Linq;
using System.Linq.Expressions;
using System.Collections.Generic;
using System;

namespace DLinq
{
    public static class SqlQueryExtensions
    {
        public static SqlQuery<T> FromFunction<T>(this SqlQuery<T> source, string functionName, params object[] args)
        {
            var method = typeof(SqlQueryExtensions).GetMethod(nameof(FromFunction)).MakeGenericMethod(typeof(T));
            var call = Expression.Call(
                null,
                method,
                source.Expression,
                Expression.Constant(functionName),
                Expression.NewArrayInit(typeof(object), args.Select(Expression.Constant))
            );
            return (SqlQuery<T>)source.Provider.CreateQuery<T>(call);
        }

        public static (string sql, object parameters) ToSql(this IQueryable queryable)
        {
            if (queryable.Provider is QueryProvider provider)
            {
                var sql = provider.Translator.Translate(queryable.Expression, out var parameters);
                var dict = new Dictionary<string, object>();
                for (int i = 0; i < parameters.Count; i++)
                {
                    dict[$"p{i}"] = parameters[i];
                }
                var anonParams = QueryTranslator.ToAnonymousObject(dict);
                return (sql, anonParams);
            }
            throw new NotSupportedException("ToSql is only supported for SqlQuery using QueryProvider.");
        }

        public static object ToAnonymousObject(Dictionary<string, object> dict)
        {
            // Use QueryTranslator's implementation
            return QueryTranslator.ToAnonymousObject(dict);
        }

        public static SqlQuery<T> ToSqlQuery<T>(this IQueryable<T> source)
        {
            return (SqlQuery<T>)source;
        }
    }

    public static class QueryableExtensions
    {
        // Surrogate entity join overload for two joins
        public static SqlQuery<JoinResult<JoinResult<T, T2>, T3>> Join<T, T2, T3>(this SqlQuery<JoinResult<T, T2>> query, Expression<Func<T, T2, T3, bool>> onPredicate)
        {
            if (onPredicate == null) throw new ArgumentNullException(nameof(onPredicate));
            Expression<Func<JoinResult<T, T2>, T3, bool>> wrapped = (prevJoin, t3) =>
                onPredicate.Compile()(prevJoin.Left, prevJoin.Right, t3);
            return query.Join<T3>(wrapped);
        }

        // Surrogate entity join overload for three joins
        public static SqlQuery<JoinResult<JoinResult<JoinResult<T, T2>, T3>, T4>> Join<T, T2, T3, T4>(this SqlQuery<JoinResult<JoinResult<T, T2>, T3>> query, Expression<Func<T, T2, T3, T4, bool>> onPredicate)
        {
            if (onPredicate == null) throw new ArgumentNullException(nameof(onPredicate));
            Expression<Func<JoinResult<JoinResult<T, T2>, T3>, T4, bool>> wrapped = (prevJoin, t4) =>
                onPredicate.Compile()(
                    prevJoin.Left.Left, // T
                    prevJoin.Left.Right, // T2
                    prevJoin.Right, // T3
                    t4 // T4
                );
            return query.Join<T4>(wrapped);
        }

        // Surrogate Select for two joins
        public static SqlQuery<TResult> Select<T, T2, T3, TResult>(this SqlQuery<JoinResult<JoinResult<T, T2>, T3>> query, Expression<Func<T, T2, T3, TResult>> selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            Expression<Func<JoinResult<JoinResult<T, T2>, T3>, TResult>> wrapped = join =>
                selector.Compile()(join.Left.Left, join.Left.Right, join.Right);
            return query.Select(wrapped);
        }

        // Surrogate Select for three joins
        public static SqlQuery<TResult> Select<T, T2, T3, T4, TResult>(this SqlQuery<JoinResult<JoinResult<JoinResult<T, T2>, T3>, T4>> query, Expression<Func<T, T2, T3, T4, TResult>> selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            Expression<Func<JoinResult<JoinResult<JoinResult<T, T2>, T3>, T4>, TResult>> wrapped = join =>
                selector.Compile()(
                    join.Left.Left.Left, // T
                    join.Left.Left.Right, // T2
                    join.Left.Right, // T3
                    join.Right // T4
                );
            return query.Select(wrapped);
        }

        // Simplified join: only leftmost and right entity
        public static SqlQuery<JoinResult<JoinResult<TLeft, TPrev>, TRight>> Join<TLeft, TPrev, TRight>(this SqlQuery<JoinResult<TLeft, TPrev>> query, Expression<Func<TLeft, TRight, bool>> onPredicate)
        {
            if (onPredicate == null) throw new ArgumentNullException(nameof(onPredicate));
            Expression<Func<JoinResult<TLeft, TPrev>, TRight, bool>> wrapped = (prevJoin, right) =>
                onPredicate.Compile()(prevJoin.Left, right);
            return query.Join<TRight>(wrapped);
        }

        // For three joins
        public static SqlQuery<JoinResult<JoinResult<JoinResult<TLeft, TPrev>, TPrev2>, TRight>> Join<TLeft, TPrev, TPrev2, TRight>(this SqlQuery<JoinResult<JoinResult<TLeft, TPrev>, TPrev2>> query, Expression<Func<TLeft, TRight, bool>> onPredicate)
        {
            if (onPredicate == null) throw new ArgumentNullException(nameof(onPredicate));
            Expression<Func<JoinResult<JoinResult<TLeft, TPrev>, TPrev2>, TRight, bool>> wrapped = (prevJoin, right) =>
                onPredicate.Compile()(prevJoin.Left.Left, right);
            return query.Join<TRight>(wrapped);
        }

        // You can add more overloads for deeper chains if needed.
    }
}