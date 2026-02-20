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
            var options = new DLinq.InsertOptions { TableName = "CustomTable" };
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
            var options = new DLinq.UpdateOptions { TableName = "CustomTable" };
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

        public class DummyEntity1 { public int Age { get; set; } }
        public class DummyEntity2 { public string Name { get; set; } }

        [TestMethod]
        public void ParseWhere_From_BuildPredicate_Expression()
        {
            // Arrange
            var translator = new QueryTranslator(new SqlServerDialect());
            var filters = new[]
            {
                new FilterCriteria(typeof(DummyEntity1), nameof(DummyEntity1.Age), ExpressionType.GreaterThan, 18),
                new FilterCriteria(typeof(DummyEntity2), nameof(DummyEntity2.Name), ExpressionType.Equal, "Bob")
            };
            var lambda = SqlQuery<DummyEntity1>.BuildPredicate(filters, "AND");

            // Act
            var context = new QueryTranslator.TranslateContext { dialect = new SqlServerDialect() };
            var parameters = new List<object>();
            var whereSql = (string)typeof(QueryTranslator)
                .GetMethod("ParseWhere", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(translator, new object[] { lambda, parameters, context });

            Console.WriteLine("Generated WHERE clause: " + whereSql);
            // Assert
            Assert.IsTrue(whereSql.Contains("[t1].[Age] > @p0"));
            Assert.IsTrue(whereSql.Contains("[t2].[Name] = @p1"));
            Assert.AreEqual(2, parameters.Count);
            Assert.AreEqual(18, parameters[0]);
            Assert.AreEqual("Bob", parameters[1]);
        }
    }
}
