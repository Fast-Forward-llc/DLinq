using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using static Dapper.SqlMapper;

namespace DLinq
{
    public class SqlString(ISqlDialect sqlDialect)
    {
        public string Format(FormattableString formattableString)
        {
            return formattableString.ToString(sqlDialect.SqlFormatter);
        }


        public string TableName<T>()
        {
            return sqlDialect.FormatTable(QueryTranslator.GetEntityTableName(typeof(T)));
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
                var (colName, colTableName, colEntityType) = QueryTranslator.GetColumnInfo(memberExpr, new QueryTranslator.TranslateContext());
                if (colName == colAlias) colAlias = null;
                return string.IsNullOrWhiteSpace(colAlias) ? $"{sqlDialect.FormatIdentifier(colName)}" : $"{sqlDialect.FormatIdentifier(colName)} AS {sqlDialect.FormatIdentifier(colAlias)}";
            }
                //return memberExpr.Member.Name;

            throw new ArgumentException("Expression must be a simple member access", nameof(prop));
        }
    }
}
