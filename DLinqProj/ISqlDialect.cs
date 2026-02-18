using System.Collections.Generic;
using System.Dynamic;

namespace DLinq
{
    public interface ISqlDialect
    {
        string FormatTable(string tableName, string? alias = null);
        string FormatColumn(string columnName, string? tableAlias = null, bool isLiteralValue = false);
        string FormatIdentifier(string identifier);
        string ParameterPlaceholder(int index);
        string SelectStatement(SqlSelectNode ast, List<object> parameters);
        string InsertStatement(string tableName, List<string> columns, List<string> paramNames, InsertOptions options);
        string UpdateStatement(string tableName, object setValues, object whereValues, UpdateOptions options, List<(string colName, object value)> primaryKeys);
        string DeleteStatement(string tableName, object whereValues);
        string IdentityValueExpression(string tableName, string columnName);

        public IFormatProvider SqlFormatter { get; }
    }
}