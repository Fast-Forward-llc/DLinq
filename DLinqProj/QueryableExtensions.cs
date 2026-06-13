using System.Linq;
using System.Linq.Expressions;
using System.Collections.Generic;
using System;

namespace DLinq
{
    public static class SqlQueryExtensions
    {
        public static (string sql, object parameters) ToSql(this SqlQuery queryable, QueryOptions? options = null)
        {
            if (queryable.Provider is QueryProvider provider)
            {
                var sql = provider.Translator.Translate(queryable.selectNode, out var parameters, options);
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

        public static SqlQuery<T> ToSqlQuery<T>(this SqlQuery<T> source)
        {
            return (SqlQuery<T>)source;
        }
    }
}