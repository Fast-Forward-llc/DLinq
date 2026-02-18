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

            var prevJoinParam = Expression.Parameter(typeof(JoinResult<T, T2>), "prevJoin");
            var t3Param = Expression.Parameter(typeof(T3), "t3");

            var map = new Dictionary<ParameterExpression, Expression>
            {
                [onPredicate.Parameters[0]] = Expression.Property(prevJoinParam, "Left"),
                [onPredicate.Parameters[1]] = Expression.Property(prevJoinParam, "Right"),
                [onPredicate.Parameters[2]] = t3Param
            };

            var replacedBody = new ParameterReplacer(map).Visit(onPredicate.Body);
            var wrapped = Expression.Lambda<Func<JoinResult<T, T2>, T3, bool>>(replacedBody, prevJoinParam, t3Param);

            return query.Join<T3>(wrapped);
        }

        // Surrogate entity join overload for three joins (4-arg onPredicate)
        public static SqlQuery<JoinResult<JoinResult<JoinResult<T, T2>, T3>, T4>> Join<T, T2, T3, T4>(this SqlQuery<JoinResult<JoinResult<T, T2>, T3>> query, Expression<Func<T, T2, T3, T4, bool>> onPredicate)
        {
            if (onPredicate == null) throw new ArgumentNullException(nameof(onPredicate));

            var prevJoinParam = Expression.Parameter(typeof(JoinResult<JoinResult<T, T2>, T3>), "prevJoin");
            var t4Param = Expression.Parameter(typeof(T4), "t4");

            var map = new Dictionary<ParameterExpression, Expression>
            {
                [onPredicate.Parameters[0]] = Expression.Property(Expression.Property(prevJoinParam, "Left"), "Left"),
                [onPredicate.Parameters[1]] = Expression.Property(Expression.Property(prevJoinParam, "Left"), "Right"),
                [onPredicate.Parameters[2]] = Expression.Property(prevJoinParam, "Right"),
                [onPredicate.Parameters[3]] = t4Param
            };

            var replacedBody = new ParameterReplacer(map).Visit(onPredicate.Body);
            var wrapped = Expression.Lambda<Func<JoinResult<JoinResult<T, T2>, T3>, T4, bool>>(replacedBody, prevJoinParam, t4Param);

            return query.Join<T4>(wrapped);
        }

        // Surrogate Select for two joins
        public static SqlQuery<TResult> Select<T, T2, T3, TResult>(this SqlQuery<JoinResult<JoinResult<T, T2>, T3>> query, Expression<Func<T, T2, T3, TResult>> selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));

            var joinParam = Expression.Parameter(typeof(JoinResult<JoinResult<T, T2>, T3>), "join");

            // map selector parameters -> join.Left.Left, join.Left.Right, join.Right
            var map = new Dictionary<ParameterExpression, Expression>
            {
                [selector.Parameters[0]] = Expression.Property(Expression.Property(joinParam, "Left"), "Left"),
                [selector.Parameters[1]] = Expression.Property(Expression.Property(joinParam, "Left"), "Right"),
                [selector.Parameters[2]] = Expression.Property(joinParam, "Right")
            };

            var replacedBody = new ParameterReplacer(map).Visit(selector.Body);
            var wrapped = Expression.Lambda<Func<JoinResult<JoinResult<T, T2>, T3>, TResult>>(replacedBody, joinParam);
            return query.Select(wrapped);
        }

        // Surrogate Select for three joins
        public static SqlQuery<TResult> Select<T, T2, T3, T4, TResult>(this SqlQuery<JoinResult<JoinResult<JoinResult<T, T2>, T3>, T4>> query, Expression<Func<T, T2, T3, T4, TResult>> selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));

            var joinParam = Expression.Parameter(typeof(JoinResult<JoinResult<JoinResult<T, T2>, T3>, T4>), "join");

            var map = new Dictionary<ParameterExpression, Expression>
            {
                [selector.Parameters[0]] = Expression.Property(Expression.Property(Expression.Property(joinParam, "Left"), "Left"), "Left"),
                [selector.Parameters[1]] = Expression.Property(Expression.Property(Expression.Property(joinParam, "Left"), "Left"), "Right"),
                [selector.Parameters[2]] = Expression.Property(Expression.Property(joinParam, "Left"), "Right"),
                [selector.Parameters[3]] = Expression.Property(joinParam, "Right")
            };

            var replacedBody = new ParameterReplacer(map).Visit(selector.Body);
            var wrapped = Expression.Lambda<Func<JoinResult<JoinResult<JoinResult<T, T2>, T3>, T4>, TResult>>(replacedBody, joinParam);
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

        // simple helper: replaces ParameterExpression keys with specified Expression values
        private class ParameterReplacer : ExpressionVisitor
        {
            private readonly Dictionary<ParameterExpression, Expression> _map;
            public ParameterReplacer(Dictionary<ParameterExpression, Expression> map) { _map = map ?? new Dictionary<ParameterExpression, Expression>(); }
            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (_map.TryGetValue(node, out var replacement)) return replacement;
                return base.VisitParameter(node);
            }
        }

        public static SqlQuery<JoinResult<T1, T2>> Where<T1, T2>(
            this SqlQuery<JoinResult<T1, T2>> query,
            Expression<Func<T1, T2, bool>> predicate)
        {
            Expression<Func<JoinResult<T1, T2>, bool>> wrapped = j =>
                predicate.Compile()(j.Left, j.Right);
            return query.Where(wrapped);
        }

        public static SqlQuery<JoinResult<JoinResult<T1, T2>, T3>> Where<T1, T2, T3>(
            this SqlQuery<JoinResult<JoinResult<T1, T2>, T3>> query,
            Expression<Func<T1, T2, T3, bool>> predicate)
        {
            Expression<Func<JoinResult<JoinResult<T1, T2>, T3>, bool>> wrapped = j =>
                predicate.Compile()(j.Left.Left, j.Left.Right, j.Right);
            return query.Where(wrapped);
        }

        public static SqlQuery<JoinResult<JoinResult<JoinResult<T1, T2>, T3>, T4>> Where<T1, T2, T3, T4>(
            this SqlQuery<JoinResult<JoinResult<JoinResult<T1, T2>, T3>, T4>> query,
            Expression<Func<T1, T2, T3, T4, bool>> predicate)
        {
            Expression<Func<JoinResult<JoinResult<JoinResult<T1, T2>, T3>, T4>, bool>> wrapped = j =>
                predicate.Compile()(
                    j.Left.Left.Left,
                    j.Left.Left.Right,
                    j.Left.Right,
                    j.Right
                );
            return query.Where(wrapped);
        }
    }
}