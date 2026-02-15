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
            var queryType2 = typeof(SqlQuery<>).MakeGenericType(elementType);
            return (IQueryable)Activator.CreateInstance(queryType2, this, expression);
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
}