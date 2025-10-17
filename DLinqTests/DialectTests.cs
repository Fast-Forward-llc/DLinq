using Microsoft.VisualStudio.TestTools.UnitTesting;
using DLinq;
using System.Collections.Generic;
using System.Dynamic;

namespace DLinqTests
{
    [TestClass]
    public class SqlServerDialectTests
    {
        private SqlServerDialect dialect = new SqlServerDialect();

        [TestMethod]
        public void FormatTable_QuotesSchemaAndTable()
        {
            var result = dialect.FormatTable("dbo.Table");
            Assert.AreEqual("[dbo].[Table]", result);
        }

        [TestMethod]
        public void FormatColumn_QuotesColumn()
        {
            var result = dialect.FormatColumn("ColName");
            Assert.AreEqual("[ColName]", result);
        }

        [TestMethod]
        public void ParameterPlaceholder_ReturnsCorrectFormat()
        {
            Assert.AreEqual("@p0", dialect.ParameterPlaceholder(0));
            Assert.AreEqual("@p5", dialect.ParameterPlaceholder(5));
        }

        [TestMethod]
        public void SelectStatement_BasicSelect()
        {
            var ast = new SqlSelectNode { Table = "T", Columns = new List<string> { "A", "B" } };
            var sql = dialect.SelectStatement(ast, new List<object>());
            StringAssert.StartsWith(sql, "SELECT [A], [B] FROM [T]");
        }

        [TestMethod]
        public void InsertStatement_BasicInsert()
        {
            var sql = dialect.InsertStatement("T", new List<string> { "A", "B" }, new List<string> { "@A", "@B" }, new DLinq.Options());
            Assert.AreEqual("INSERT INTO [T] ([A], [B]) VALUES (@A, @B)", sql);
        }

        [TestMethod]
        public void UpdateStatement_BasicUpdate()
        {
            var sql = dialect.UpdateStatement("T", new { A = 1, C = "Qwerty" }, new { B = 2 }, new DLinq.Options(), new List<(string, object)>());
            Assert.AreEqual("UPDATE [T] SET [A] = @A, [C] = @C WHERE [B] = @B", sql);
        }

        [TestMethod]
        public void DeleteStatement_BasicDelete()
        {
            var sql = dialect.DeleteStatement("T", new { A = 1 });
            Assert.AreEqual("DELETE FROM [T] WHERE [A] = @A", sql);
        }
    }

    [TestClass]
    public class PostgresDialectTests
    {
        private PostgresDialect dialect = new PostgresDialect();

        [TestMethod]
        public void FormatTable_QuotesSchemaAndTable()
        {
            var result = dialect.FormatTable("public.Table");
            Assert.AreEqual("\"public\".\"Table\"", result);
        }

        [TestMethod]
        public void FormatColumn_QuotesColumn()
        {
            var result = dialect.FormatColumn("ColName");
            Assert.AreEqual("\"ColName\"", result);
        }

        [TestMethod]
        public void ParameterPlaceholder_ReturnsCorrectFormat()
        {
            Assert.AreEqual("@p0", dialect.ParameterPlaceholder(0));
            Assert.AreEqual("@p5", dialect.ParameterPlaceholder(5));
        }

        [TestMethod]
        public void SelectStatement_BasicSelect()
        {
            var ast = new SqlSelectNode { Table = "T", Columns = new List<string> { "A", "B" } };
            var sql = dialect.SelectStatement(ast, new List<object>());
            StringAssert.StartsWith(sql, "SELECT \"A\", \"B\" FROM \"T\"");
        }

        [TestMethod]
        public void InsertStatement_BasicInsert()
        {
            var sql = dialect.InsertStatement("T", new List<string> { "A", "B" }, new List<string> { "@A", "@B" }, new DLinq.Options());
            Assert.AreEqual("INSERT INTO \"T\" (\"A\", \"B\") VALUES (@A, @B)", sql);
        }

        [TestMethod]
        public void UpdateStatement_BasicUpdate()
        {
            var sql = dialect.UpdateStatement("T", new { A = 1, C = "Qwerty" }, new { B = 2 }, new DLinq.Options(), new List<(string, object)>());
            Assert.AreEqual("UPDATE \"T\" SET \"A\" = @A, \"C\" = @C WHERE \"B\" = @B", sql);
        }

        [TestMethod]
        public void DeleteStatement_BasicDelete()
        {
            var sql = dialect.DeleteStatement("T", new { A = 1 });
            Assert.AreEqual("DELETE FROM \"T\" WHERE \"A\" = @A", sql);
        }
    }
}
