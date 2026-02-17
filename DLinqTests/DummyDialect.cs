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
        public string FormatColumn(string columnName, string? tableName = null, bool isLiteralValue = false) => isLiteralValue ? columnName : $"\"{columnName}\"";
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
        public string IdentityValueExpression(string tableName, string columnName) => "<identity>";
    }
}
