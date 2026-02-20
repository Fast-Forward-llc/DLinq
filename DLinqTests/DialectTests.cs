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
            var ast = new SqlSelectNode
            {
                FromTable = "T",
                Columns = new List<Column>
                {
                    new Column(null, null, "A"),
                    new Column(null, null, "B")
                }
            };
            var sql = dialect.SelectStatement(ast, new List<object>());
            Console.WriteLine(sql);
            StringAssert.StartsWith(sql, "SELECT [A], [B] FROM [T]");
        }

        [TestMethod]
        public void SelectStatement_BasicSelect_WithTableAlias()
        {
            var ast = new SqlSelectNode
            {
                FromTable = "T",
                TableAlias = "T1",
                Columns = new List<Column>
                {
                    new Column(null, null, "A"),
                    new Column(null, null, "B")
                }
            };
            var sql = dialect.SelectStatement(ast, new List<object>());
            Console.WriteLine(sql);
            StringAssert.StartsWith(sql, "SELECT [T1].[A], [T1].[B] FROM [T] AS [T1]");
        }

        [TestMethod]
        public void InsertStatement_BasicInsert()
        {
            var sql = dialect.InsertStatement("T", new List<string> { "A", "B" }, new List<string> { "@A", "@B" }, new DLinq.InsertOptions());
            Console.WriteLine(sql); 
            Assert.AreEqual("INSERT INTO [T] ([A], [B]) VALUES (@A, @B)", sql);
        }

        [TestMethod]
        public void UpdateStatement_BasicUpdate()
        {
            var sql = dialect.UpdateStatement("T", new { A = 1, C = "Qwerty" }, new { B = 2 }, new DLinq.UpdateOptions(), new List<(string, object)>());
            Console.WriteLine(sql);
            Assert.AreEqual("UPDATE [T] SET [A] = @A, [C] = @C\r\nWHERE [B] = @B", sql);
        }

        [TestMethod]
        public void DeleteStatement_BasicDelete()
        {
            var sql = dialect.DeleteStatement("T", new { A = 1 });
            Console.WriteLine(sql);
            Assert.AreEqual("DELETE FROM [T]\r\nWHERE [A] = @A", sql);
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
            var ast = new SqlSelectNode
            {
                FromTable = "T",
                Columns = new List<Column>
                {
                    new Column(null, null, "A"),
                    new Column(null, null, "B")
                }
            };
            var sql = dialect.SelectStatement(ast, new List<object>());
            StringAssert.StartsWith(sql, "SELECT \"A\", \"B\" FROM \"T\"");
        }

        [TestMethod]
        public void SelectStatement_BasicSelect_WithTableAlias()
        {
            var ast = new SqlSelectNode
            {
                FromTable = "T",
                TableAlias = "T1",
                Columns = new List<Column>
                {
                    new Column(null, null, "A"),
                    new Column(null, null, "B")
                }
            };
            var sql = dialect.SelectStatement(ast, new List<object>());
            Console.WriteLine(sql);
            StringAssert.StartsWith(sql, "SELECT \"T1\".\"A\", \"T1\".\"B\" FROM \"T\" AS \"T1\"");
        }

        [TestMethod]
        public void InsertStatement_BasicInsert()
        {
            var sql = dialect.InsertStatement("T", new List<string> { "A", "B" }, new List<string> { "@A", "@B" }, new DLinq.InsertOptions());
            Console.WriteLine(sql);
            Assert.AreEqual("INSERT INTO \"T\" (\"A\", \"B\") VALUES (@A, @B)", sql);
        }

        [TestMethod]
        public void UpdateStatement_BasicUpdate()
        {
            var sql = dialect.UpdateStatement("T", new { A = 1, C = "Qwerty" }, new { B = 2 }, new DLinq.UpdateOptions(), new List<(string, object)>());
            Console.WriteLine(sql);
            Assert.AreEqual("UPDATE \"T\" SET \"A\" = @A, \"C\" = @C\r\nWHERE \"B\" = @B", sql);
        }

        [TestMethod]
        public void DeleteStatement_BasicDelete()
        {
            var sql = dialect.DeleteStatement("T", new { A = 1 });
            Console.WriteLine(sql); 
            Assert.AreEqual("DELETE FROM \"T\"\r\nWHERE \"A\" = @A", sql);
        }
    }
}
