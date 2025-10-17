using Microsoft.VisualStudio.TestTools.UnitTesting;
using DLinq;
using System;
using System.Linq.Expressions;
using System.Collections.Generic;

namespace DLinqTests
{
    [TestClass]
    public class QueryTranslatorTests
    {
        [TestMethod]
        public void Constructor_SetsDialect()
        {
            var dialect = new DummyDialect();
            var translator = new QueryTranslator(dialect);
            Assert.AreEqual(dialect, translator.Dialect);
        }

        [TestMethod]
        public void GenerateInsertSql_UsesTableNameFromOptions()
        {
            var dialect = new DummyDialect();
            var translator = new QueryTranslator(dialect);
            var entity = new { Id = 1, Name = "Test" };
            var options = new DLinq.Options { TableName = "CustomTable" };
            var result = translator.GenerateInsertSql(entity, options);
            // Should use options.TableName
            Assert.IsTrue(result.sql.Contains("CustomTable"));
        }

        [TestMethod]
        public void GenerateUpdateSql_UsesTableNameFromOptions()
        {
            var dialect = new DummyDialect();
            var translator = new QueryTranslator(dialect);
            var entity = new { Id = 1, Name = "Test" };
            var options = new DLinq.Options { TableName = "CustomTable" };
            var result = translator.GenerateUpdateSql(entity, options);
            Assert.IsTrue(result.sql.Contains("CustomTable"));
        }

        [TestMethod]
        public void GenerateDeleteSql_UsesTableNameFromOptions()
        {
            var dialect = new DummyDialect();
            var translator = new QueryTranslator(dialect);
            var options = new DLinq.Options { TableName = "CustomTable" };
            var result = translator.GenerateDeleteSql(typeof(object), null, options);
            Assert.IsTrue(result.sql.Contains("CustomTable"));
        }

        private class DummyDialect : ISqlDialect
        {
            public string FormatTable(string tableName) => tableName;
            public string FormatColumn(string columnName) => columnName;
            public string ParameterPlaceholder(int index) => "@p" + index;
            public string SelectStatement(SqlSelectNode ast, List<object> parameters) => "SELECT";
            public string InsertStatement(string tableName, List<string> columns, List<string> paramNames, DLinq.Options options)
            {
                return $"INSERT INTO {tableName}";
            }
            public string UpdateStatement(string tableName, object setValues, object whereValues, DLinq.Options options, List<(string colName, object value)> primaryKeys)
            {
                return $"UPDATE {options?.TableName ?? tableName}";
            }
            public string DeleteStatement(string tableName, object whereValues)
            {
                return $"DELETE FROM {tableName}";
            }
            public string IdentityValueExpression(string tableName, string columnName)
            {
                return $"<identity>";
            }
        }
    }
}
