using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using static Dapper.SqlMapper;

namespace DLinq
{
    public class SqlString
    {
        protected readonly ISqlDialect _sqlDialect;
        protected readonly QueryTranslator _queryTranslator;

        public SqlString(ISqlDialect sqlDialect)
        {
            _sqlDialect = sqlDialect;
            _queryTranslator = new QueryTranslator(sqlDialect);
        }

        public SqlString(ISqlDialect sqlDialect, QueryTranslator queryTranslator)
        {
            _sqlDialect = sqlDialect;
            _queryTranslator = queryTranslator;
        }

        public string Format(FormattableString formattableString)
        {
            return formattableString.ToString(_sqlDialect.SqlFormatter);
        }


        public string TableName<T>()
        {
            return _sqlDialect.FormatTable(_queryTranslator.GetEntityTableName(typeof(T)));
        }

        public string ColumnName<TEntity>(Expression<Func<TEntity, object?>> prop)
        {
            // Unwrap conversion if present (e.g., x => (object)x.Property)
            Expression body = prop.Body;
            if (body is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
                body = unary.Operand;

            if (body is MemberExpression memberExpr)
            {
                var colAlias = memberExpr.Member.Name;
                var (colName, colTableName, colEntityType) = _queryTranslator.GetColumnInfo(memberExpr, new QueryTranslator.TranslateContext());
                if (colName == colAlias) colAlias = null;
                return string.IsNullOrWhiteSpace(colAlias) ? $"{_sqlDialect.FormatIdentifier(colName)}" : $"{_sqlDialect.FormatIdentifier(colName)} AS {_sqlDialect.FormatIdentifier(colAlias)}";
            }
                //return memberExpr.Member.Name;

            throw new ArgumentException("Expression must be a simple member access", nameof(prop));
        }
    }
}
