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

        private class TestDialect : DummyDialect
        {
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

        [TestMethod]
        public void Join_GeneratesSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId)
                .Select<Person, Pet>((p, pt) => new { PersonName = p.Name, PetName = pt.Name });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT [t1].[Name] AS [PersonName], [t2].[Name] AS [PetName] FROM [Person] AS [t1]"
                +" INNER JOIN [Pet] AS [t2] ON [t1].[Id] = [t2].[OwnerId]"
                ,sql);
        }

        [TestMethod]
        public void Join_WithMultipleConditions_GeneratesSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .Where(person => person.Age > 18)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId && pet.Name != null)
                .Select<Person, Pet>((p, pt) => new { PersonName = p.Name, PetName = pt.Name });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT [t1].[Name] AS [PersonName], [t2].[Name] AS [PetName] FROM [Person] AS [t1]"
                +" INNER JOIN [Pet] AS [t2] ON ([t1].[Id] = [t2].[OwnerId]) AND ([t2].[Name] IS NOT NULL) "
                +"WHERE [t1].[Age] > @p0"
                ,sql);
            Assert.IsTrue(sql.Contains("ON"));
            Assert.IsTrue(sql.Contains("AND"));
        }

        [TestMethod]
        public void Join_WithMultipleConditions_ToPersonProjection_GeneratesSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId && pet.Name != null)
                .Where(p => p.Age > 18)
                .Select<Pet>((p) => new Person{ Name = p.Name, Age = 4 });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT [t2].[Name] AS [Name], 4 AS [Age] FROM [Person] AS [t1]"
                +" INNER JOIN [Pet] AS [t2] ON ([t1].[Id] = [t2].[OwnerId]) AND ([t2].[Name] IS NOT NULL) "
                +"WHERE [t1].[Age] > @p0"
                ,sql);
        }

        [TestMethod]
        public void Join_WithMultipleConditions_ToPerson_GeneratesSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId && pet.Name != null)
                .Select((p) => new Person { Name = p.Name, Age = 4 });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.IsTrue(sql.Contains("JOIN"));
            Assert.IsTrue(sql.Contains("ON"));
            Assert.IsTrue(sql.Contains("AND"));
        }

        [TestMethod]
        public void Join_ChainedWithWhereAndSelect_GeneratesSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId)
                .Where<Pet>((p,pt) => p.Age > 18 && pt.Name != null)
                .Select<Person, Pet>((p, pt) => new { PersonName = p.Name, PetName = pt.Name });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT [t1].[Name] AS [PersonName], [t2].[Name] AS [PetName] FROM [Person] AS [t1]"
                +" INNER JOIN [Pet] AS [t2] ON [t1].[Id] = [t2].[OwnerId] "
                + "WHERE ([t1].[Age] > @p0) AND ([t2].[Name] IS NOT NULL)"
                , sql);
        }

        [Table("DummyTable")]
        private class TestEntity
        {
            [Key]
            public int Id { get; set; }
            public string Name { get; set; }
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
