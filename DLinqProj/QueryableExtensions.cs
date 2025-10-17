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
}