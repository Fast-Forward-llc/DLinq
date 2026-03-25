using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace DLinq
{
    public class PostgresDialect : ISqlDialect
    {
        public enum DialectOptions
        {
            None = 0,
            ForceLowerCase = 1,
            ForceLowerSnakeCase = 2
        }

        public PostgresDialect() { }
        public PostgresDialect(DialectOptions options) { _options = options; }

        private DialectOptions _options;

        public string FormatTable(string tableName, string? alias = null)
        {
            if (string.IsNullOrWhiteSpace(tableName)) return tableName;
            if (tableName.StartsWith("\"") && tableName.EndsWith("\"")) tableName = tableName.Substring(0, tableName.Length - 1).Substring(1);
            var formatted = string.Join(".", tableName.Split('.').Select(part => FormatIdentifier(part)));
            if (!string.IsNullOrEmpty(alias))
                return $"{formatted} AS \"{alias}\"";
            return formatted;
        }

        public string FormatIdentifier(string identifier)
        {
            return FormatIdentifierQuoted(identifier, _options);
        }

        public static string FormatIdentifierQuoted(string identifier, DialectOptions options)
        {
            return $"\"{FormatIdentifierUnquoted(identifier, options)}\"";
        }

        public static string FormatIdentifierUnquoted(string identifier, DialectOptions options)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return string.Empty;
            switch (options)
            {
                case DialectOptions.ForceLowerCase: identifier = identifier.ToLower(); break;
                case DialectOptions.ForceLowerSnakeCase:
                    {
                        identifier = ToLowerSnakeCase(identifier);
                    }
                    break;
            }
            identifier = identifier.Replace("\"", "\"\"");
            return identifier;
        }

        public string FormatParameter(string paramName)
        {
            return FormatParameterInternal(paramName);
        }

        private static string FormatParameterInternal(string paramName)
        {
            return $"@{paramName}";
        }

        public string FormatValue(object? value)
        {
            if (value == null) return "NULL";
            if (value is string s) return $"'{s.Replace("'", "''")}'";
            if (value is bool b) return b ? "1" : "0";
            if (value is DateTime dt) return $"'{dt:yyyy-MM-ddTHH:mm:ss.fffffff}'";
            if (value is Guid g) return $"'{g}'";
            if (value is Enum) return Convert.ToInt32(value).ToString();
            // Check if it's a numeric type (int, long, decimal, double, etc.)
            var typeCode = Type.GetTypeCode(value.GetType());
            if (typeCode >= TypeCode.SByte && typeCode <= TypeCode.Decimal)
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "NULL";
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "NULL";
        }

        public string FormatColumn(string columnName, string? tableAlias = null, bool isLiteralValue = false)
        {
            if (string.IsNullOrWhiteSpace(columnName)) return columnName;
            if (isLiteralValue) return columnName;
            string escapedColumnName = FormatIdentifier(columnName);
            if (!string.IsNullOrWhiteSpace(tableAlias))
                return $"{FormatTable(tableAlias)}.{escapedColumnName}";
            return escapedColumnName;
        }

        private string QuotedIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return string.Empty;
            if (identifier.StartsWith("\"") && identifier.EndsWith("\""))
                return identifier;
            return $"\"{identifier}\"";
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
                    var col = FormatColumn(c.Name, c.Table ?? ast.TableAlias, c.IsLiteralValue);
                    var alias = string.IsNullOrEmpty(c.Alias) ? "" : $" AS {FormatColumn(c.Alias)}";
                    return $"{col}{alias}";
                })));
            }
            else
            {
                sb.Append("*");
            }
            sb.Append(" FROM ");
            if (ast.FromFunction != null)
            {
                var args = ast.FromFunction.Arguments.Count > 0
                    ? string.Join(", ", ast.FromFunction.Arguments.Select(a => a is string s ? $"'{s}'" : a.ToString()))
                    : "";
                sb.Append($"{ast.FromFunction.FunctionName}({args})");
            }
            else
            {
                sb.Append(FormatTable(ast.FromTable, ast.TableAlias));
            }
            // JOIN support
            if (ast.Joins != null)
            {
                foreach (var join in ast.Joins)
                {
                    sb.Append($" {join.JoinType.ToUpper()} JOIN ");
                    sb.Append(FormatTable(join.RightTable, join.RightAlias));
                    sb.Append(" ON ");
                    sb.Append(join.OnClause);
                }
            }
            if (!string.IsNullOrEmpty(ast.WhereSqlExpr))
            {
                sb.Append(ast.WhereSqlExpr);
            }
            if (ast.OrderBy != null && ast.OrderBy.Count > 0)
            {
                sb.Append(" ORDER BY ");
                sb.Append(string.Join(", ", ast.OrderBy.Select(o => $"{FormatColumn(o.Column.Name, o.Column.Table)}{(o.Descending ? " DESC" : " ASC")}")));
            }
            if (ast.Take.HasValue)
            {
                sb.Append($" LIMIT {ast.Take.Value}");
            }
            if (ast.Skip.HasValue)
            {
                sb.Append($" OFFSET {ast.Skip.Value}");
            }
            return sb.ToString();
        }

        public string InsertStatement(string tableName, List<string> columns, List<string> paramNames, InsertOptions options)
        {
            var quotedColumns = columns.Select(col => FormatColumn(col));
            var sql = $"INSERT INTO {FormatTable(tableName)} ({string.Join(", ", quotedColumns)}) VALUES ({string.Join(", ", paramNames)})";
            if (options.SelectAfterMutation)
            {
                sql += " RETURNING *;";
            }
            return sql;
        }

        public string UpdateStatement(string tableName, object setValues, object whereValues, UpdateOptions options, List<(string colName, object value)> primaryKeys)
        {
            var setDict = setValues is IDictionary<string, object> dictSet ? dictSet : setValues.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(setValues));
            var whereDict = whereValues is IDictionary<string, object> dictWhere ? dictWhere : whereValues.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(whereValues));
            var setClauses = setDict.Select(kvp => $"{FormatColumn(kvp.Key)} = {FormatParameter(kvp.Key)}");
            var whereClauses = whereDict.Select(kvp => $"{FormatColumn(kvp.Key)} = {FormatParameter(kvp.Key)}");
            var sql = $"UPDATE {FormatTable(tableName)} SET {string.Join(", ", setClauses)}{WhereClauseFromFragments(whereClauses)}";
            if (options.SelectAfterMutation && primaryKeys.Count > 0)
            {
                //var selectWhere = string.Join(" AND ", primaryKeys.Select(pk => $"{FormatColumn(pk.colName)} = {FormatParameter(pk.colName)}"));
                sql += $" RETURNING *;";
            }
            return sql;
        }

        public string UpdateStatement(string tableName, Dictionary<string, string> setClause, string? whereClause, UpdateOptions options)
        {
            // Build SET clause from dictionary (column name -> parameter placeholder)
            var setClauses = setClause.Select(kvp => $"{FormatColumn(kvp.Key)} = {kvp.Value}");
            var sql = $"UPDATE {FormatTable(tableName)} SET {string.Join(", ", setClauses)}";

            // Add WHERE clause if provided
            if (!string.IsNullOrWhiteSpace(whereClause))
            {
                sql += $"\r\nWHERE {whereClause}";
            }

            // Postgres: RETURNING comes AFTER WHERE
            if (options.SelectAfterMutation)
            {
                sql += " RETURNING *;";
            }

            return sql;
        }

        public string DeleteStatement(string tableName, object whereValues)
        {
            var whereDict = whereValues is IDictionary<string, object> dictWhere ? dictWhere : whereValues.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(whereValues));
            var whereClauses = whereDict.Select(kvp => $"{FormatColumn(kvp.Key)} = {FormatParameter(kvp.Key)}");
            var sql = $"DELETE FROM {FormatTable(tableName)}";
            sql += WhereClauseFromFragments(whereClauses);

            return sql;
        }

        public string WhereClauseFromFragments(IEnumerable<string> clauseFragments, string logicalOperator = "AND")
        {
            if (clauseFragments == null || !clauseFragments.Any()) return string.Empty;
            return "\r\nWHERE " + string.Join($" {logicalOperator} ", clauseFragments);
        }
        public string IdentityValueExpression(string tableName, string columnName)
        {
            // Returns the last inserted identity value for the given table and column
            var sql = $"currval(pg_get_serial_sequence('{tableName?.ToLower()}', '{columnName?.ToLower()}'))";
            return sql;
        }

        public static string ToLowerSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var sb = new StringBuilder();
            bool prevIsLowerOrDigit = false;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (char.IsUpper(c))
                {
                    if (i > 0 && prevIsLowerOrDigit)
                        sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                    prevIsLowerOrDigit = false;
                }
                else if (char.IsWhiteSpace(c) || c == '-' || c == '.')
                {
                    sb.Append('_');
                    prevIsLowerOrDigit = false;
                }
                else
                {
                    sb.Append(c);
                    prevIsLowerOrDigit = char.IsLower(c) || char.IsDigit(c);
                }
            }
            // Remove consecutive underscores
            var result = sb.ToString();
            while (result.Contains("__"))
                result = result.Replace("__", "_");
            // Trim leading/trailing underscores
            return result.Trim('_');
        }

        public string MapExpressionTypeToSqlOperator(ExpressionType expressionType)
        {
            return expressionType switch
            {
                ExpressionType.Equal => "=",
                ExpressionType.NotEqual => "<>",
                ExpressionType.GreaterThan => ">",
                ExpressionType.GreaterThanOrEqual => ">=",
                ExpressionType.LessThan => "<",
                ExpressionType.LessThanOrEqual => "<=",
                ExpressionType.AndAlso => "AND",
                ExpressionType.OrElse => "OR",
                ExpressionType.Add => "+",
                ExpressionType.Subtract => "-",
                ExpressionType.Multiply => "*",
                ExpressionType.Divide => "/",
                ExpressionType.Modulo => "%",
                ExpressionType.And => "&",
                ExpressionType.Or => "|",
                ExpressionType.Coalesce => "COALESCE",
                ExpressionType.ExclusiveOr => "||",
                _ => throw new NotSupportedException($"Expression type '{expressionType}' is not supported.")
            };
        }

        private IFormatProvider? sqlFormatProvider;

        public IFormatProvider SqlFormatter
        {
            get
            {
                if (sqlFormatProvider == null)
                    sqlFormatProvider = new SqlFormatProvider(_options);
                return sqlFormatProvider;
            }
        }

        public sealed class SqlFormatProvider(DialectOptions Options) : IFormatProvider, ICustomFormatter
        {

            public object? GetFormat(Type? formatType)
                => formatType == typeof(ICustomFormatter) ? this : null;

            public string Format(string? format, object? arg, IFormatProvider? provider)
            {
                if (string.Equals(format, "I", StringComparison.OrdinalIgnoreCase))
                    return arg == null ? "" : PostgresDialect.FormatIdentifierQuoted(arg.ToString(), Options);
                if (string.Equals(format, "P", StringComparison.OrdinalIgnoreCase))
                    return arg == null ? "" : PostgresDialect.FormatParameterInternal(arg.ToString());
                // Fallback
                return arg is IFormattable f
                    ? f.ToString(format, provider)
                    : arg?.ToString() ?? string.Empty;
            }
        }

    }
}