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

        public string FormatTable(string tableName)
        {
            // Split schema-qualified names and quote each part
            return string.Join(".", tableName.Split('.').Select(part => FormatOptions($"\"{part.Replace("\"", "\"\"")}\"")));
        }

        public string FormatOptions(string identifier)
        {
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

        public string FormatColumn(string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return columnName;
            string escaped = FormatOptions(columnName.Replace("\"", "\"\""));
            return QuotedIdentifier(escaped);
        }

        public string FormatColumnWithAlias(string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return columnName;
            string colName = FormatColumn(columnName);
            string colNameRaw = QuotedIdentifier(columnName);
            if (colName == colNameRaw) return colName;
            return $"{colName} AS {colNameRaw}";
        }

        private string QuotedIdentifier(string identifier)
        {
            if (identifier.StartsWith("\"") && identifier.EndsWith("\""))
                return identifier;
            return $"\"{identifier}\"";
        }

        public string ParameterPlaceholder(int index) => $"@p{index}";

        public string SelectStatement(SqlSelectNode ast, List<object> parameters)
        {
            var sb = new StringBuilder();
            sb.Append("SELECT ");
            sb.Append(string.Join(", ", ast.Columns.Count > 0 ? ast.Columns.ConvertAll(FormatColumn) : new[] { "*" }));
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
                sb.Append(FormatTable(ast.Table));
            }
            if (ast.Joins != null && ast.Joins.Count > 0)
            {
                foreach (var join in ast.Joins)
                {
                    sb.Append($" {join.JoinType} JOIN {FormatTable(join.Table)} ON {FormatColumn(join.LeftColumn)} = {FormatColumn(join.RightColumn)}");
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
                sb.Append(string.Join(", ", ast.OrderBy.Select(o => $"{FormatColumn(o.Column)}{(o.Descending ? " DESC" : " ASC")}")));
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

        public string InsertStatement(string tableName, List<string> columns, List<string> paramNames, Options options)
        {
            var quotedColumns = columns.Select(col => FormatColumn(col));
            var sql = $"INSERT INTO {FormatTable(tableName)} ({string.Join(", ", quotedColumns)}) VALUES ({string.Join(", ", paramNames)})";
            return sql;
        }

        public string UpdateStatement(string tableName, object setValues, object whereValues, Options options, List<(string colName, object value)> primaryKeys)
        {
            var setDict = setValues is IDictionary<string, object> dictSet ? dictSet : setValues.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(setValues));
            var whereDict = whereValues is IDictionary<string, object> dictWhere ? dictWhere : whereValues.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(whereValues));
            var setClauses = setDict.Select(kvp => $"{FormatColumn(kvp.Key)} = @{kvp.Key}");
            var whereClauses = whereDict.Select(kvp => $"{FormatColumn(kvp.Key)} = @{kvp.Key}");
            var sql = $"UPDATE {FormatTable(tableName)} SET {string.Join(", ", setClauses)} WHERE {string.Join(" AND ", whereClauses)}";
            if (options.SelectAfterMutation && primaryKeys.Count > 0)
            {
                var selectWhere = string.Join(" AND ", primaryKeys.Select(pk => $"{FormatColumn(pk.colName)} = @{pk.colName}"));
                sql += $"; SELECT * FROM {FormatTable(tableName)} WHERE {selectWhere}";
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