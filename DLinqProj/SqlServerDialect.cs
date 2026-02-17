using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Dynamic;

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
        private static string EscapeInnerSquareBrackets(string input)
        {
            if (string.IsNullOrEmpty(input) || input.Length < 3)
                return input;
            if (input.LastIndexOf("[")==0 && input.LastIndexOf("]")== input.Length-1) return input;
            var sb = new StringBuilder(input.Length+3);
            sb.Append(input[0]);
            for (int i = 1; i < input.Length - 1; i++)
            {
                if (input[i] == '[' || input[i] == ']')
                {
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
    }
}
