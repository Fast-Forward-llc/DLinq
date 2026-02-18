using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using static DLinq.PostgresDialect;

namespace DLinq
{
    public class SqlServerDialect : ISqlDialect
    {
        public string FormatTable(string tableName, string? alias = null)
        {
            if (string.IsNullOrWhiteSpace(tableName)) return tableName;
            var formatted = string.Join(".", tableName.Split('.').Select(part => $"[{part.Replace("]", "]]")}]"));
            if (!string.IsNullOrEmpty(alias))
                return $"{formatted} AS [{alias}]";
            return formatted;
        }

        public string FormatTableRaw(string tableName, string? alias = null)
        {
            if (string.IsNullOrWhiteSpace(tableName)) return tableName;
            var formatted = string.Join(".", tableName.Split('.').Select(part => $"{part.Replace("]", "]]")}"));
            if (!string.IsNullOrEmpty(alias))
                return alias;
            return formatted;
        }

        public string FormatColumn(string columnName, string? tableName = null, bool isLiteralValue = false)
        {
            if (string.IsNullOrEmpty(columnName)) return columnName;
            if (isLiteralValue) return columnName;
            string escapedColumnName = EscapeInnerSquareBrackets(columnName);
            string escapedTableName = FormatTable(tableName!);
            if (!string.IsNullOrWhiteSpace(tableName) && !(escapedTableName.StartsWith('[') && escapedTableName.EndsWith(']')))
                escapedTableName = $"[{escapedTableName}]";
            if (!string.IsNullOrWhiteSpace(columnName) && !(escapedColumnName.StartsWith('[') && escapedColumnName.EndsWith(']')))
                escapedColumnName = $"[{escapedColumnName}]";
            if (!string.IsNullOrWhiteSpace(tableName))
                return $"{escapedTableName}.{escapedColumnName}";
            return $"{escapedColumnName}";
        }
        public string FormatIdentifier(string identifier)
        {
            return FormatIdentifierQuoted(identifier);
        }
        public static string FormatIdentifierQuoted(string identifier)
        {
            return $"[{EscapeInnerSquareBrackets(identifier)}]";
        }

        public static string FormatParameter(string paramName)
        {
            return $"@{paramName}";
        }

        public string FormatValue(object? value)
        {
            if (value is null) return "NULL";
            else if (value is string s) return $"'{s.Replace("'", "''")}'";
            else if (value is DateTime dt) return $"'{dt:yyyy-MM-dd HH:mm:ss.fff}'";
            else if (value is bool b) return b ? "1" : "0";
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "NULL";
        }

        private static string EscapeInnerSquareBrackets(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            if (input == "[]") return input;
            if (input.StartsWith("[") && input.EndsWith("]")) input = input.Substring(0, input.Length - 1).Substring(1);
            input = input.Replace("]", "]]");
            return $"{input}";
        }

        public string ParameterPlaceholder(int index) => $"@p{index}";

        public string SelectStatement(SqlSelectNode ast, List<object> parameters)
        {
            var sb = new StringBuilder();
            sb.Append("SELECT ");
            if (ast.Columns.Count > 0)
            {
                sb.Append(string.Join(", ", ast.Columns.Select(c =>
                {
                    var col = FormatColumn(c.Name, c.Table ?? ast.Alias, c.IsLiteralValue);
                    var alias = string.IsNullOrEmpty(c.Alias) ? "" : $" AS {FormatColumn(c.Alias)}";
                    return $"{col}{alias}";
                })));
            }
            else
            {
                sb.Append("*");
            }
            sb.Append(" FROM ");
            sb.Append(FormatTable(ast.Table, ast.Alias));
            // JOIN support
            if (ast is SqlJoinSelectNode joinAst && joinAst.Joins != null)
            {
                foreach (var join in joinAst.Joins)
                {
                    sb.Append($" {join.JoinType.ToUpper()} JOIN ");
                    sb.Append(FormatTable(join.RightTable, join.RightAlias));
                    sb.Append(" ON ");
                    sb.Append(join.OnClause);
                }
            }
            if (!string.IsNullOrEmpty(ast.WhereSql))
            {
                sb.Append(" WHERE ");
                sb.Append(ast.WhereSql);
            }
            if (ast.OrderBy != null && ast.OrderBy.Count > 0)
            {
                sb.Append(" ORDER BY ");
                sb.Append(string.Join(", ", ast.OrderBy.Select(o => $"{FormatColumn(o.Column, ast.Alias)}{(o.Descending ? " DESC" : " ASC")}")));
            }
            if (ast.Take.HasValue)
            {
                sb.Append($" OFFSET 0 ROWS FETCH NEXT {ast.Take.Value} ROWS ONLY");
            }
            return sb.ToString();
        }

        public string InsertStatement(string tableName, List<string> columns, List<string> paramNames, InsertOptions options)
        {
            var quotedColumns = columns.Select(col => FormatColumn(col));
            var sql = $"INSERT INTO {FormatTable(tableName)} ({string.Join(", ", quotedColumns)})"; 
            if (options.SelectAfterMutation)
            {
                sql += $" OUTPUT inserted.*";
            }
            sql += $" VALUES ({string.Join(", ", paramNames)})";
            return sql;
        }

        public string UpdateStatement(string tableName, object setValues, object whereValues, UpdateOptions options, List<(string colName, object value)> primaryKeys)
        {
            var setDict = setValues is IDictionary<string, object> dictSet ? dictSet : setValues.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(setValues));
            var whereDict = whereValues is IDictionary<string, object> dictWhere ? dictWhere : whereValues.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(whereValues));
            var setClauses = setDict.Select(kvp => $"{FormatColumn(kvp.Key)} = @{kvp.Key}");
            var whereClauses = whereDict.Select(kvp => $"{FormatColumn(kvp.Key)} = @{kvp.Key}");
            var sql = $"UPDATE {FormatTable(tableName)} SET {string.Join(", ", setClauses)}";
            if (options.SelectAfterMutation)
            {
                sql += $" OUTPUT inserted.*";
            }
            sql += whereClauses.Any() ? $" WHERE {string.Join(" AND ", whereClauses)}" : "";
            return sql;
        }

        public string DeleteStatement(string tableName, object whereValues)
        {
            var whereDict = whereValues is IDictionary<string, object> dictWhere ? dictWhere : whereValues.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(whereValues));
            var whereClauses = whereDict.Select(kvp => $"{FormatColumn(kvp.Key)} = @{kvp.Key}");
            var sql = $"DELETE FROM {FormatTable(tableName)}";
            if (whereClauses.Any())
            {
                sql += $" WHERE {string.Join(" AND ", whereClauses)}";
            }
            return sql;
        }

        public string IdentityValueExpression(string tableName, string columnName)
        {
            return "SCOPE_IDENTITY()";
        }

        private IFormatProvider? sqlFormatProvider;

        public IFormatProvider SqlFormatter
        {
            get
            {
                if (sqlFormatProvider == null)
                    sqlFormatProvider = new SqlFormatProvider();
                return sqlFormatProvider;
            }
        }

        public sealed class SqlFormatProvider() : IFormatProvider, ICustomFormatter
        {

            public object? GetFormat(Type? formatType)
                => formatType == typeof(ICustomFormatter) ? this : null;

            public string Format(string? format, object? arg, IFormatProvider? provider)
            {
                if (string.Equals(format, "I", StringComparison.OrdinalIgnoreCase))
                    return arg == null ? "" : SqlServerDialect.FormatIdentifierQuoted(arg.ToString());
                if (string.Equals(format, "P", StringComparison.OrdinalIgnoreCase))
                    return arg == null ? "" : SqlServerDialect.FormatParameter(arg.ToString());

                // Fallback
                return arg is IFormattable f
                    ? f.ToString(format, provider)
                    : arg?.ToString() ?? string.Empty;
            }
        }
    }
}
