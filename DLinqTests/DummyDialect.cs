using DLinq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DLinqTests
{
    public class DummyDialect : ISqlDialect
    {
        public string FormatTable(string tableName) => tableName;
        public string FormatTable(string tableName, string? alias) => string.IsNullOrEmpty(alias) ? tableName : $"{tableName} AS {alias}";
        public string FormatIdentifier(string identifier) => $"\"{identifier}\"";
        public string FormatColumn(string columnName, string? tableName = null, bool isLiteralValue = false) => isLiteralValue ? columnName : $"\"{columnName}\"";
        public string FormatValue(object? value) => value?.ToString();
        public string FormatParameter(string paramName) => "@" + paramName;
        public string ParameterPlaceholder(int index) => "@p" + index;
        public string SelectStatement(SqlSelectNode ast, List<object> parameters) => "SELECT";
        public string DeleteStatement(string tableName, object whereValues)
        {
            return $"DELETE FROM {tableName}";
        }

        public string InsertStatement(string tableName, System.Collections.Generic.List<string> columns, System.Collections.Generic.List<string> paramNames, DLinq.InsertOptions options)
        {
            return $"INSERT INTO {tableName}";
        }

        public string UpdateStatement(string tableName, object setValues, object whereValues, DLinq.UpdateOptions options, System.Collections.Generic.List<(string colName, object value)> primaryKeys)
        {
            return $"UPDATE {options?.TableName ?? tableName}";
        }

        public string UpdateStatement(string tableName, Dictionary<string, string> setClause, string? whereClause, DLinq.UpdateOptions options)
        {
            var setClauses = setClause.Select(kvp => $"{FormatColumn(kvp.Key)} = {kvp.Value}");
            var sql = $"UPDATE {FormatTable(tableName)} SET {string.Join(", ", setClauses)}";
            if (!string.IsNullOrWhiteSpace(whereClause))
            {
                sql += $"\r\nWHERE {whereClause}";
            }
            return sql;
        }

        public string WhereClauseFromFragments(IEnumerable<string> clauseFragments, string logicalOperator = "AND")
        {
            return "\r\nWHERE "+string.Join($" {logicalOperator} ", clauseFragments);
        }
        public string IdentityValueExpression(string tableName, string columnName) => "<identity>";
        public IFormatProvider SqlFormatter => null!;
        public string MapExpressionTypeToSqlOperator(System.Linq.Expressions.ExpressionType expressionType)
        {
            return expressionType switch
            {
                System.Linq.Expressions.ExpressionType.Equal => "=",
                System.Linq.Expressions.ExpressionType.NotEqual => "<>",
                System.Linq.Expressions.ExpressionType.GreaterThan => ">",
                System.Linq.Expressions.ExpressionType.GreaterThanOrEqual => ">=",
                System.Linq.Expressions.ExpressionType.LessThan => "<",
                System.Linq.Expressions.ExpressionType.LessThanOrEqual => "<=",
                System.Linq.Expressions.ExpressionType.AndAlso => "AND",
                System.Linq.Expressions.ExpressionType.OrElse => "OR",
                _ => throw new NotSupportedException($"Expression type {expressionType} is not supported.")
            };
        }
        public (string? Schema, string Table) ParseTableName(string tableName) => (null, tableName);
    }
}
