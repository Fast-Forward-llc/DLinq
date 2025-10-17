using Microsoft.VisualStudio.TestTools.UnitTesting;
using DLinq;
using System.Collections.Generic;

namespace DLinqTests
{
    [TestClass]
    public class ISqlDialectContractTests
    {
        [TestMethod]
        public void DummyDialect_ImplementsAllMethods()
        {
            var dialect = new DummyDialect();
            Assert.AreEqual("T", dialect.FormatTable("T"));
            Assert.AreEqual("C", dialect.FormatColumn("C"));
            Assert.AreEqual("@p0", dialect.ParameterPlaceholder(0));
            Assert.AreEqual("SELECT", dialect.SelectStatement(new SqlSelectNode(), new List<object>()));
            Assert.AreEqual("INSERT", dialect.InsertStatement("T", new List<string>(), new List<string>(), new DLinq.Options()));
            Assert.AreEqual("UPDATE", dialect.UpdateStatement("T", new { A = 1 }, new { B = 2 }, new DLinq.Options(), new List<(string, object)>()));
            Assert.AreEqual("DELETE", dialect.DeleteStatement("T", new { A = 1 }));
            Assert.AreEqual("<identity>", dialect.IdentityValueExpression("T", "Id"));
        }

        private class DummyDialect : ISqlDialect
        {
            public string FormatTable(string tableName) => tableName;
            public string FormatColumn(string columnName) => columnName;
            public string ParameterPlaceholder(int index) => "@p" + index;
            public string SelectStatement(SqlSelectNode ast, List<object> parameters) => "SELECT";
            public string InsertStatement(string tableName, List<string> columns, List<string> paramNames, DLinq.Options options) => "INSERT";
            public string UpdateStatement(string tableName, object setValues, object whereValues, DLinq.Options options, List<(string colName, object value)> primaryKeys) => "UPDATE";
            public string DeleteStatement(string tableName, object whereValues) => "DELETE";
            public string IdentityValueExpression(string tableName, string columnName)
            {
                return $"<identity>";
            }
        }
    }
}
