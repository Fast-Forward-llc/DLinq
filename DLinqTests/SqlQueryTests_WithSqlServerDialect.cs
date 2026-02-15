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
    public class SqlQueryTests_WithSqlServerDialect
    {
        private QueryProvider GetProvider() => new QueryProvider(new SqlServerDialect());

        private class Person
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int Age { get; set; }
        }

        private class Pet
        {
            public int Id { get; set; }
            public int OwnerId { get; set; }
            public string Name { get; set; }
        }

        private class PersonPet
        {
            public string PersonName { get; set; }
            public string PetName { get; set; }
        }

        private class TestDialect : ISqlDialect
        {
            public string FormatTable(string tableName) => tableName;
            public string FormatTable(string tableName, string? alias) => string.IsNullOrEmpty(alias) ? tableName : $"{tableName} AS {alias}";
            public string FormatColumn(string columnName, string? tableName = null) => columnName;
            public string ParameterPlaceholder(int index) => "@p" + index;
            public string SelectStatement(SqlSelectNode ast, List<object> parameters) => "SELECT";
            public string InsertStatement(string tableName, List<string> columns, List<string> paramNames, DLinq.InsertOptions options) => "INSERT";
            public string UpdateStatement(string tableName, object setValues, object whereValues, DLinq.UpdateOptions options, List<(string colName, object value)> primaryKeys) => "UPDATE";
            public string DeleteStatement(string tableName, object whereValues) => "DELETE";
            public string IdentityValueExpression(string tableName, string columnName) => "<identity>";
        }

        [TestMethod]
        public void Where_GeneratesSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Where(x => x.Age > 18);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.IsTrue(sql.Contains("WHERE [t1].[Age] > @p0"));
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
            Console.WriteLine(sql);
            Assert.IsTrue(sql.Contains("SELECT"));
        }

        [TestMethod]
        public void SkipTake_GeneratesSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Skip(5).Take(10);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.IsTrue(sql.Contains("SELECT"));
        }

        [TestMethod]
        public void ToInsertSql_GeneratesSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider);
            var (sql, parameters) = query.ToInsertSql(new Person { Name = "Test", Age = 20 });
            Console.WriteLine(sql);
            Assert.IsTrue(sql.StartsWith("INSERT INTO"));
        }

        [TestMethod]
        public void ToInsertSql_DynamicSchemaAndTable_GeneratesSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider);
            var options = new DLinq.InsertOptions { TableName = "customschema.CustomPerson" };
            var (sql, parameters) = query.ToInsertSql(new Person { Name = "Dynamic", Age = 42 }, options);
            Console.WriteLine(sql);
            Assert.IsTrue(sql.Contains("customschema"));
            Assert.IsTrue(sql.Contains("CustomPerson"));
            Assert.IsTrue(sql.StartsWith("INSERT INTO"));
        }

        [TestMethod]
        public void ToUpdateSql_GeneratesSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider);
            var (sql, parameters) = query.ToUpdateSql(new Person { Id = 1, Name = "Test", Age = 21 });
            Console.WriteLine(sql);
            Assert.IsTrue(sql.StartsWith("UPDATE"));
        }

        [TestMethod]
        public void ToDeleteSql_GeneratesSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider);
            var (sql, parameters) = query.ToDeleteSql(x => x.Id == 1);
            Console.WriteLine(sql);
            Assert.IsTrue(sql.StartsWith("DELETE FROM"));
        }

        [TestMethod]
        public void ToInsertSql_GeneratesFullSql()
        {
            var query = new SqlQuery<TestEntity>(GetProvider());
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
            var query = new SqlQuery<TestEntity>(GetProvider());
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
            var query = new SqlQuery<TestEntity>(GetProvider());
            var (sql, parameters) = query.ToDeleteSql(x => x.Id == 1);
            StringAssert.Contains(sql, "DELETE FROM [DummyTable]");
            StringAssert.Contains(sql, "WHERE [Id] = @p0");
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(1, paramDict["@p0"]);
        }

        [Table("DummyTable")]
        private class TestEntity
        {
            [Key]
            public int Id { get; set; }
            public string Name { get; set; }
        }

       
        [TestMethod]
        public void Join_WithWhere_GeneratesSql()
        {
            var provider = GetProvider();
            var people = new SqlQuery<Person>(provider);
            var pets = new SqlQuery<Pet>(provider);
            var joined = people.Join(
                pets,
                person => person.Id,
                pet => pet.OwnerId,
                (person, pet) => new PersonPet { PersonName = person.Name, PetName = pet.Name }
            ).Where(x => x.PersonName == "Alice");
            var (sql, parameters) = joined.ToSql();
            Assert.IsTrue(sql.Contains("WHERE"));
            Assert.IsTrue(sql.Contains("PersonName"));
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual("Alice", paramDict["p0"]);
        }

        private class Department
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        private class Employee
        {
            public int Id { get; set; }
            public int DepartmentId { get; set; }
            public string Name { get; set; }
        }

        private class EmployeeDepartment
        {
            public string EmployeeName { get; set; }
            public string DepartmentName { get; set; }
        }

        
        private class Order
        {
            public int Id { get; set; }
            public int CustomerId { get; set; }
            public int ProductId { get; set; }
        }
        private class Customer
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }
        private class Product
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }
        private class OrderInfo
        {
            public string CustomerName { get; set; }
            public string ProductName { get; set; }
        }
        private class OrderCustomer
        {
            public Order o { get; set; }
            public Customer c { get; set; }
        }
        private class OrderCustomerProduct
        {
            public OrderCustomer oc { get; set; }
            public Product p { get; set; }
        }

    }
}
