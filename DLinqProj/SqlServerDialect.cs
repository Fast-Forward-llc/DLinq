using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
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
            if (ast.Distinct) sb.Append("DISTINCT ");
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
                    ? string.Join(",", ast.FromFunction.Arguments.Select((a, i) => $"@p{i}"))
                    : "";
                var fnFormatted = $"{FormatIdentifier(ast.FromFunction.FunctionName)}({args})";
                if (!string.IsNullOrEmpty(ast.TableAlias))
                    fnFormatted += $" AS [{ast.TableAlias}]";
                sb.Append(fnFormatted);
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
            var setClauses = setDict.Select(kvp => $"{FormatColumn(kvp.Key)} = {FormatParameter(kvp.Key)}");
            var whereClauses = whereDict.Select(kvp => $"{FormatColumn(kvp.Key)} = {FormatParameter(kvp.Key)}");
            var sql = $"UPDATE {FormatTable(tableName)} SET {string.Join(", ", setClauses)}";
            if (options.SelectAfterMutation)
            {
                sql += $" OUTPUT inserted.*";
            }
            sql += WhereClauseFromFragments(whereClauses);
            return sql;
        }

        public string UpdateStatement(string tableName, Dictionary<string, string> setClause, string? whereClause, UpdateOptions options)
        {
            // Build SET clause from dictionary (column name -> parameter placeholder)
            var setClauses = setClause.Select(kvp => $"{FormatColumn(kvp.Key)} = {kvp.Value}");
            var sql = $"UPDATE {FormatTable(tableName)} SET {string.Join(", ", setClauses)}";

            // SQL Server: OUTPUT comes BEFORE WHERE
            if (options.SelectAfterMutation)
            {
                sql += " OUTPUT inserted.*";
            }

            // Add WHERE clause if provided
            if (!string.IsNullOrWhiteSpace(whereClause))
            {
                sql += $"\r\nWHERE {whereClause}";
            }

            return sql;
        }

        public string DeleteStatement(string tableName, object whereValues)
        {
            IEnumerable<string> whereClauses = Enumerable.Empty<string>();
            if (whereValues != null)
            {
                var whereDict = whereValues is IDictionary<string, object> dictWhere ? dictWhere : whereValues.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(whereValues));
                whereClauses = whereDict.Select(kvp => $"{FormatColumn(kvp.Key)} = {FormatParameter(kvp.Key)}");
            }
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
            return "SCOPE_IDENTITY()";
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
                    return arg == null ? "" : SqlServerDialect.FormatParameterInternal(arg.ToString());

                // Fallback
                return arg is IFormattable f
                    ? f.ToString(format, provider)
                    : arg?.ToString() ?? string.Empty;
            }
        }
    }
}
