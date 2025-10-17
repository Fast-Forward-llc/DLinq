using Microsoft.VisualStudio.TestTools.UnitTesting;
using DLinq;
using System.Linq.Expressions;

namespace DLinqTests
{
    [TestClass]
    public class QueryProviderTests
    {
        [TestMethod]
        public void Constructor_SetsDialect()
        {
            var dialect = new TestDialect();
            var provider = new QueryProvider(dialect);
            Assert.AreEqual(dialect, provider.Translator.Dialect);
        }

        private class TestDialect : ISqlDialect
        {
            public string FormatTable(string tableName) => tableName;
            public string FormatColumn(string columnName) => columnName;
            public string ParameterPlaceholder(int index) => "@p" + index;
            public string SelectStatement(SqlSelectNode ast, System.Collections.Generic.List<object> parameters) => "SELECT";
            
            public string DeleteStatement(string tableName, object whereValues)
            {
                return $"DELETE FROM {tableName}";
            }

            public string InsertStatement(string tableName, List<string> columns, List<string> paramNames, DLinq.Options options)
            {
                return $"INSERT INTO {tableName}";
            }

            public string UpdateStatement(string tableName, object setValues, object whereValues, DLinq.Options options, List<(string colName, object value)> primaryKeys)
            {
                return $"UPDATE {options?.TableName ?? tableName}";
            }

            public string IdentityValueExpression(string tableName, string columnName)
            {
                return $"<identity>";
            }
        }
    }
}
