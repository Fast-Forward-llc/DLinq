using Microsoft.VisualStudio.TestTools.UnitTesting;
using DLinq;
using System;
using System.Linq.Expressions;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Dynamic;
using System.Linq;

namespace DLinqTests
{
    [TestClass]
    public class SqlQueryTests
    {
        private QueryProvider GetProvider() => new QueryProvider(new TestDialect());

        private class Person
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int Age { get; set; }
        }

        private class TestDialect : ISqlDialect
        {
            public string FormatTable(string tableName) => tableName;
            public string FormatColumn(string columnName) => columnName;
            public string ParameterPlaceholder(int index) => "@p" + index;
            public string SelectStatement(SqlSelectNode ast, List<object> parameters) => $"SELECT FROM {ast.Table} WHERE {ast.WhereSql}";
            public string InsertStatement(string tableName, List<string> columns, List<string> paramNames, DLinq.Options options)
            {
                return $"INSERT INTO {tableName}";
            }
            public string UpdateStatement(string tableName, object setValues, object whereValues, DLinq.Options options, List<(string colName, object value)> primaryKeys) => $"UPDATE {tableName}";
            public string DeleteStatement(string tableName, object whereValues) => $"DELETE FROM {tableName}";
            public string IdentityValueExpression(string tableName, string columnName)
            {
                return $"<identity>";
            }
        }

        [TestMethod]
        public void Where_GeneratesSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Where(x => x.Age > 18);
            var (sql, parameters) = query.ToSql();
            Assert.IsTrue(sql.Contains("WHERE"));
            Assert.IsTrue(sql.Contains("Age"));
            Assert.AreEqual(1, ((IDictionary<string, object>)parameters).Count);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(18, paramDict["p0"]);
        }

        [TestMethod]
        public void OrderBy_GeneratesSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).OrderBy(x => x.Name);
            var (sql, parameters) = query.ToSql();
            Assert.IsTrue(sql.Contains("SELECT"));
        }

        [TestMethod]
        public void SkipTake_GeneratesSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Skip(5).Take(10);
            var (sql, parameters) = query.ToSql();
            Assert.IsTrue(sql.Contains("SELECT"));
        }

        [TestMethod]
        public void ToInsertSql_GeneratesSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider);
            var (sql, parameters) = query.ToInsertSql(new Person { Name = "Test", Age = 20 });
            Assert.IsTrue(sql.StartsWith("INSERT INTO"));
        }

        [TestMethod]
        public void ToUpdateSql_GeneratesSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider);
            var (sql, parameters) = query.ToUpdateSql(new Person { Id = 1, Name = "Test", Age = 21 });
            Assert.IsTrue(sql.StartsWith("UPDATE"));
        }

        [TestMethod]
        public void ToDeleteSql_GeneratesSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider);
            var (sql, parameters) = query.ToDeleteSql(x => x.Id == 1);
            Assert.IsTrue(sql.StartsWith("DELETE FROM"));
        }

        [TestMethod]
        public void ToInsertSql_GeneratesFullSql()
        {
            var query = new SqlQuery<TestEntity>(new QueryProvider(new SqlServerDialect()));
            var (sql, parameters) = query.ToInsertSql(new TestEntity { Id = 1, Name = "abc" });
            StringAssert.Contains(sql, "INSERT INTO [DummyTable]");
            StringAssert.Contains(sql, "[Id]");
            StringAssert.Contains(sql, "[Name]");
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(1, paramDict["@Id"]);
            Assert.AreEqual("abc", paramDict["@Name"]);
        }

        [TestMethod]
        public void ToUpdateSql_GeneratesFullSql()
        {
            var query = new SqlQuery<TestEntity>(new QueryProvider(new SqlServerDialect()));
            var (sql, parameters) = query.ToUpdateSql(new TestEntity { Id = 1, Name = "abc" });
            StringAssert.Contains(sql, "UPDATE [DummyTable]");
            StringAssert.Contains(sql, "SET [Name] = @Name");
            StringAssert.Contains(sql, "WHERE [Id] = @Id");
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(1, paramDict["@Id"]);
            Assert.AreEqual("abc", paramDict["@Name"]);
        }

        [TestMethod]
        public void ToDeleteSql_GeneratesFullSql_WithPredicate()
        {
            var query = new SqlQuery<TestEntity>(new QueryProvider(new SqlServerDialect()));
            var (sql, parameters) = query.ToDeleteSql(x => x.Id == 1);
            StringAssert.Contains(sql, "DELETE FROM [DummyTable]");
            StringAssert.Contains(sql, "WHERE [Id] = @Id");
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(1, paramDict["@Id"]);
        }

        [Table("DummyTable")]
        private class TestEntity
        {
            [Key]
            public int Id { get; set; }
            public string Name { get; set; }
        }
    }
}
