using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Dynamic;

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
            if (tableName.StartsWith("\"")) tableName = tableName.Substring(1);
            if (tableName.EndsWith("\"")) tableName = tableName.Substring(0, tableName.Length - 1);
            var formatted = string.Join(".", tableName.Split('.').Select(part => FormatIdentifier($"\"{part.Replace("\"", "\"\"")}\"")));
            if (!string.IsNullOrEmpty(alias))
                return $"{formatted} AS \"{alias}\"";
            return formatted;
        }

        public string FormatTableRaw(string tableName, string? alias = null)
        {
            if (string.IsNullOrWhiteSpace(tableName)) return tableName;
            if (tableName.StartsWith("\"")) tableName = tableName.Substring(1);
            if (tableName.EndsWith("\"")) tableName = tableName.Substring(0, tableName.Length - 1);
            var formatted = string.Join(".", tableName.Split('.').Select(part => FormatIdentifier($"{part.Replace("\"", "\"\"")}")));
            if (!string.IsNullOrEmpty(alias))
                return alias;
            return formatted;
        }

        public string FormatIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return string.Empty;
            switch (_options)
            {
                case DialectOptions.ForceLowerCase: return identifier.ToLower();
                case DialectOptions.ForceLowerSnakeCase:
                    {
                        return ToLowerSnakeCase(identifier);
                    }
                default: return identifier;
            }
        }

        public string FormatColumn(string columnName, string? tableAlias = null)
        {
            if (string.IsNullOrWhiteSpace(columnName)) return columnName;
            string escapedColumnName = FormatIdentifier(columnName.Replace("\"", "\"\""));
            if (!string.IsNullOrWhiteSpace(tableAlias))
                return $"{FormatTable(tableAlias)}.\"{escapedColumnName}\"";
            return $"\"{escapedColumnName}\"";
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
                    var col = FormatColumn(c.Name, c.Table ?? ast.Alias);
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
                sb.Append(FormatTable(ast.Table, ast.Alias));
            }
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
            var setClauses = setDict.Select(kvp => $"{FormatColumn(kvp.Key)} = @{kvp.Key}");
            var whereClauses = whereDict.Select(kvp => $"{FormatColumn(kvp.Key)} = @{kvp.Key}");
            var sql = $"UPDATE {FormatTable(tableName)} SET {string.Join(", ", setClauses)} WHERE {string.Join(" AND ", whereClauses)}";
            if (options.SelectAfterMutation && primaryKeys.Count > 0)
            {
                //var selectWhere = string.Join(" AND ", primaryKeys.Select(pk => $"{FormatColumn(pk.colName)} = @{pk.colName}"));
                sql += $" RETURNING *;";
            }
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
    }
}