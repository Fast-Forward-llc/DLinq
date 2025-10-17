using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Dynamic;

namespace DLinq
{
    public class SqlServerDialect : ISqlDialect
    {
        public string FormatTable(string tableName)
        {
            // Split schema-qualified names and quote each part
            return string.Join(".", tableName.Split('.').Select(part => $"[{part.Replace("]", "]]")}]"));
        }

        public string FormatColumn(string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return columnName;
            string escaped = EscapeInnerSquareBrackets(columnName);
            if (escaped.StartsWith("[") && escaped.EndsWith("]"))
                return escaped;
            return $"[{escaped}]";
        }
        private static string EscapeInnerSquareBrackets(string input)
        {
            if (string.IsNullOrEmpty(input) || input.Length < 3)
                return input;
            //Exit early if the only brackets are the first and last.
            if (input.LastIndexOf("[")==0 && input.LastIndexOf("]")== input.Length-1) return input;
            //Escape inner brackets
            var sb = new StringBuilder(input.Length+3);
            sb.Append(input[0]);
            for (int i = 1; i < input.Length - 1; i++)
            {
                if (input[i] == '[' || input[i] == ']')
                {
                    // Only escape if not first or last character
                    sb.Append(input[i]);
                    sb.Append(input[i]);
                }
                else
                {
                    sb.Append(input[i]);
                }
            }
            sb.Append(input[^1]);
            return sb.ToString();
        }

        public string ParameterPlaceholder(int index) => $"@p{index}";

        public string SelectStatement(SqlSelectNode ast, List<object> parameters)
        {
            var sb = new StringBuilder();
            sb.Append("SELECT ");
            sb.Append(string.Join(", ", ast.Columns.Count > 0 ? ast.Columns.ConvertAll(FormatColumn) : new[] { "*" }));
            sb.Append(" FROM ");
            sb.Append(FormatTable(ast.Table));
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
                sb.Append($" OFFSET 0 ROWS FETCH NEXT {ast.Take.Value} ROWS ONLY");
            }
            return sb.ToString();
        }

        public string InsertStatement(string tableName, List<string> columns, List<string> paramNames, Options options)
        {
            var quotedColumns = columns.Select(col => FormatColumn(col));
            return $"INSERT INTO {FormatTable(tableName)} ({string.Join(", ", quotedColumns)}) VALUES ({string.Join(", ", paramNames)})";
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
            return "SCOPE_IDENTITY()";
        }
    }
}
