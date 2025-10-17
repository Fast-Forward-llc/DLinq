using System.Collections.Generic;
using System.Dynamic;

namespace DLinq
{
    public interface ISqlDialect
    {
        string FormatTable(string tableName);
        string FormatColumn(string columnName);
        string ParameterPlaceholder(int index);
        string SelectStatement(SqlSelectNode ast, List<object> parameters);
        string InsertStatement(string tableName, List<string> columns, List<string> paramNames, Options options);
        string UpdateStatement(string tableName, object setValues, object whereValues, Options options, List<(string colName, object value)> primaryKeys);
        string DeleteStatement(string tableName, object whereValues);
        /// <summary>
        /// Returns the SQL expression to retrieve the last inserted identity value for the given table and column.
        /// </summary>
        string IdentityValueExpression(string tableName, string columnName);
    }
}