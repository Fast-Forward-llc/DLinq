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
        public class IdNameClass
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

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
            var result = translator.GenerateInsertSql<IdNameClass>(entity, options);
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
            var result = translator.GenerateUpdateSql<IdNameClass>(entity, options);
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
            var parseWhere = typeof(QueryTranslator)
                .GetMethod("ParseWhere", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var whereSql = (string)parseWhere.Invoke(translator, new object[] { lambda, parameters, context, null });

            Console.WriteLine("Generated WHERE clause: " + whereSql);
            // Assert
            Assert.IsTrue(whereSql.Contains("[t1].[Age] > @p0"));
            Assert.IsTrue(whereSql.Contains("[t2].[Name] = @p1"));
            Assert.AreEqual(2, parameters.Count);
            Assert.AreEqual(18, parameters[0]);
            Assert.AreEqual("Bob", parameters[1]);
        }

        [TestMethod]
        public void ParseWhere_WithAdditionalAndPredicate_AppendsAndFragment()
        {
            // Arrange
            var translator = new QueryTranslator(new SqlServerDialect());
            Expression<Func<DummyEntity1, bool>> baseExpr = e => e.Age > 18;
            Expression<Func<DummyEntity1, bool>> andExpr = e => e.Age < 65;
            var additionalPredicates = new List<(LambdaExpression Expr, string LogicalOperator)>
            {
                ((LambdaExpression)andExpr, "AND")
            };

            var context = new QueryTranslator.TranslateContext { dialect = new SqlServerDialect() };
            var parameters = new List<object>();
            var parseWhere = typeof(QueryTranslator)
                .GetMethod("ParseWhere", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var whereSql = (string)parseWhere.Invoke(translator, new object[] { baseExpr, parameters, context, additionalPredicates });

            Console.WriteLine("Generated WHERE clause: " + whereSql);
            Assert.IsTrue(whereSql.Contains("[t1].[Age] > @p0"));
            Assert.IsTrue(whereSql.Contains("AND [t1].[Age] < @p1"));
            Assert.AreEqual(2, parameters.Count);
            Assert.AreEqual(18, parameters[0]);
            Assert.AreEqual(65, parameters[1]);
        }

        [TestMethod]
        public void ParseWhere_WithAdditionalOrPredicate_AppendsOrFragment()
        {
            // Arrange
            var translator = new QueryTranslator(new SqlServerDialect());
            Expression<Func<DummyEntity1, bool>> baseExpr = e => e.Age > 18;
            Expression<Func<DummyEntity1, bool>> orExpr = e => e.Age < 10;
            var additionalPredicates = new List<(LambdaExpression Expr, string LogicalOperator)>
            {
                ((LambdaExpression)orExpr, "OR")
            };

            var context = new QueryTranslator.TranslateContext { dialect = new SqlServerDialect() };
            var parameters = new List<object>();
            var parseWhere = typeof(QueryTranslator)
                .GetMethod("ParseWhere", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var whereSql = (string)parseWhere.Invoke(translator, new object[] { baseExpr, parameters, context, additionalPredicates });

            Console.WriteLine("Generated WHERE clause: " + whereSql);
            Assert.IsTrue(whereSql.Contains("[t1].[Age] > @p0"));
            Assert.IsTrue(whereSql.Contains("OR [t1].[Age] < @p1"));
            Assert.AreEqual(2, parameters.Count);
            Assert.AreEqual(18, parameters[0]);
            Assert.AreEqual(10, parameters[1]);
        }

        [TestMethod]
        public void ParseWhere_WithNoBaseExpr_FirstAdditionalBecomesBase()
        {
            // Arrange — no base WhereExpr, first additional predicate must not carry an operator prefix
            var translator = new QueryTranslator(new SqlServerDialect());
            Expression<Func<DummyEntity1, bool>> firstExpr = e => e.Age > 18;
            Expression<Func<DummyEntity1, bool>> secondExpr = e => e.Age < 65;
            var additionalPredicates = new List<(LambdaExpression Expr, string LogicalOperator)>
            {
                ((LambdaExpression)firstExpr, "AND"),
                ((LambdaExpression)secondExpr, "AND")
            };

            var context = new QueryTranslator.TranslateContext { dialect = new SqlServerDialect() };
            var parameters = new List<object>();
            var parseWhere = typeof(QueryTranslator)
                .GetMethod("ParseWhere", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var whereSql = (string)parseWhere.Invoke(translator, new object[] { null, parameters, context, additionalPredicates });

            Console.WriteLine("Generated WHERE clause: " + whereSql);
            // First predicate has no AND/OR prefix; second carries AND
            Assert.IsTrue(whereSql.StartsWith("\r\nWHERE [t1].[Age] > @p0"));
            Assert.IsTrue(whereSql.Contains("AND [t1].[Age] < @p1"));
            Assert.AreEqual(2, parameters.Count);
            Assert.AreEqual(18, parameters[0]);
            Assert.AreEqual(65, parameters[1]);
        }

        [TestMethod]
        public void ParseWhere_WithMixedAndOrPredicates_CombinesInOrder()
        {
            // Arrange
            var translator = new QueryTranslator(new SqlServerDialect());
            Expression<Func<DummyEntity1, bool>> baseExpr = e => e.Age > 18;
            Expression<Func<DummyEntity1, bool>> andExpr = e => e.Age < 65;
            Expression<Func<DummyEntity2, bool>> orExpr = e => e.Name == "Admin";
            var additionalPredicates = new List<(LambdaExpression Expr, string LogicalOperator)>
            {
                ((LambdaExpression)andExpr, "AND"),
                ((LambdaExpression)orExpr, "OR")
            };

            var context = new QueryTranslator.TranslateContext { dialect = new SqlServerDialect() };
            var parameters = new List<object>();
            var parseWhere = typeof(QueryTranslator)
                .GetMethod("ParseWhere", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var whereSql = (string)parseWhere.Invoke(translator, new object[] { baseExpr, parameters, context, additionalPredicates });

            Console.WriteLine("Generated WHERE clause: " + whereSql);
            Assert.IsTrue(whereSql.Contains("[t1].[Age] > @p0"));
            Assert.IsTrue(whereSql.Contains("AND [t1].[Age] < @p1"));
            Assert.IsTrue(whereSql.Contains("OR [t2].[Name] = @p2"));
            Assert.AreEqual(3, parameters.Count);
            Assert.AreEqual(18, parameters[0]);
            Assert.AreEqual(65, parameters[1]);
            Assert.AreEqual("Admin", parameters[2]);
        }
    }
}
