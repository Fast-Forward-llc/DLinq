using Castle.Core.Resource;
using DLinq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;

namespace DLinqTests
{
    [TestClass]
    public class SqlQueryTests_WithPostgresDialect
    {
        private QueryProvider GetProvider() => new QueryProvider(new PostgresDialect());

        private class Person
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int Age { get; set; }
            public int CreatedByUserId { get; set; } = 1;
            public int ModifiedByUserId { get; set; } = 1;
        }

        [Table("person")]
        private class Person3 : Person
        {
            public int CreatedByUserId { get; set; } = 1;
            public int ModifiedByUserId { get; set; } = 1;
        }

        [Table("Users")]
        public class CreatedByUser
        {
            [Key]
            [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
            public int Id { get; set; }
            public string Name { get; set; }
        }

        public class ModifiedByUser : CreatedByUser { }

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

        private class PersonPetJoin
        {
            public Person Person { get; set; }
            public Pet Pet { get; set; }
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
            var query = new SqlQuery<TestEntity>(new QueryProvider(new PostgresDialect()));
            var (sql, parameters) = query.ToInsertSql(new TestEntity { Id = 1, Name = "abc" });
            Console.WriteLine(sql);
            StringAssert.Contains(sql, "INSERT INTO \"DummyTable\"");
            StringAssert.Contains(sql, "\"Id\"");
            StringAssert.Contains(sql, "\"Name\"");
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(1, paramDict["@Id"]);
            Assert.AreEqual("abc", paramDict["@Name"]);
        }

        [TestMethod]
        public void ToUpdateSql_GeneratesFullSql()
        {
            var query = new SqlQuery<TestEntity>(new QueryProvider(new PostgresDialect()));
            var (sql, parameters) = query.ToUpdateSql(new TestEntity { Id = 1, Name = "abc" });
            Console.WriteLine(sql);
            StringAssert.Contains(sql, "UPDATE \"DummyTable\"");
            StringAssert.Contains(sql, "SET \"Name\" = @Name");
            StringAssert.Contains(sql, "WHERE \"Id\" = @Id");
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(1, paramDict["@Id"]);
            Assert.AreEqual("abc", paramDict["@Name"]);
        }

        [TestMethod]
        public void ToDeleteSql_GeneratesFullSql_WithPredicate()
        {
            var query = new SqlQuery<TestEntity>(new QueryProvider(new PostgresDialect()));
            var (sql, parameters) = query.ToDeleteSql(x => x.Id == 1);
            Console.WriteLine(sql);
            StringAssert.Contains(sql, "DELETE FROM \"DummyTable\"");
            StringAssert.Contains(sql, "WHERE \"Id\" = @p0");
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(1, paramDict["@p0"]);
        }

        [TestMethod]
        public void ToDeleteSql_GeneratesFullSql_WithGtPredicate()
        {
            var query = new SqlQuery<TestEntity>(new QueryProvider(new PostgresDialect()));
            var (sql, parameters) = query.ToDeleteSql(x => x.Id > 1);
            Console.WriteLine(sql);
            StringAssert.Contains(sql, "DELETE FROM \"DummyTable\"");
            StringAssert.Contains(sql, "WHERE \"Id\" > @p0");
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
                .Select(j => new { PersonName = j.Left.Name, PetName = j.Right.Name });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT \"t1\".\"Name\" AS \"PersonName\", \"t2\".\"Name\" AS \"PetName\" FROM \"Person\" AS \"t1\" "
                +"INNER JOIN \"Pet\" AS \"t2\" ON \"t1\".\"Id\" = \"t2\".\"OwnerId\"", sql);
        }

        [TestMethod]
        public void Join_WithMultipleConditions_GeneratesSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId && pet.Name != null)
                .Select(j => new { PersonName = j.Left.Name, PetName = j.Right.Name });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT \"t1\".\"Name\" AS \"PersonName\", \"t2\".\"Name\" AS \"PetName\" FROM \"Person\" AS \"t1\" "
                +"INNER JOIN \"Pet\" AS \"t2\" ON (\"t1\".\"Id\" = \"t2\".\"OwnerId\") AND (\"t2\".\"Name\" IS NOT NULL)"
                , sql);
        }

        [TestMethod]
        public void Join_ChainedWithWhereAndSelect_GeneratesSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId)
                .Where(j => j.Left.Age > 18)
                .Select(j => new { PersonName = j.Left.Name, PetName = j.Right.Name });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT \"t1\".\"Name\" AS \"PersonName\", \"t2\".\"Name\" AS \"PetName\" FROM \"Person\" AS \"t1\" "
                +"INNER JOIN \"Pet\" AS \"t2\" ON \"t1\".\"Id\" = \"t2\".\"OwnerId\" "
                +"WHERE \"t1\".\"Age\" > @p0"
                , sql);
            Assert.IsTrue(sql.Contains("WHERE"));
            Assert.IsTrue(sql.Contains("Age"));
        }

        [TestMethod]
        public void Join_MultipleOfSameTable_GeneratesSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person3>(provider)
                .Where(p => p.Age > 18)
                .Join<CreatedByUser>((person, user) => person.CreatedByUserId == user.Id)
                .Join<ModifiedByUser>((prevJoin, user) => prevJoin.Left.ModifiedByUserId == user.Id)
                .Select(j => new { PersonName = j.Left.Left.Name, CreatedByUser = j.Left.Right.Name, ModifiedByUser = j.Right.Name });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT \"t1\".\"Name\" AS \"PersonName\", \"t3\".\"Name\" AS \"CreatedByUser\", \"t2\".\"Name\" AS \"ModifiedByUser\" FROM \"person\" AS \"t1\" "
                +"INNER JOIN \"Users\" AS \"t2\" ON \"t1\".\"ModifiedByUserId\" = \"t2\".\"Id\" "
                +"INNER JOIN \"Users\" AS \"t3\" ON \"t1\".\"CreatedByUserId\" = \"t3\".\"Id\" "
                +"WHERE \"t1\".\"Age\" > @p0"
                , sql);
            Assert.IsTrue(sql.Contains("WHERE"));
            Assert.IsTrue(sql.Contains("Age"));
        }

        [TestMethod]
        public void Join_MultipleAndSetEntity_GeneratesSql()
        {
            var p = new Person3();
            var pt = new Pet();
            var cu = new CreatedByUser();
            var mu = new ModifiedByUser();
            var provider = GetProvider();
            var query = new SqlQuery<Person3>(provider)
                .Where(p => p.Age > 18)
                .Join<CreatedByUser>((person, user) => person.CreatedByUserId == user.Id)
                .Join<ModifiedByUser>((prevJoin, user) => prevJoin.Left.ModifiedByUserId == user.Id)
                //.Select(j => new { PersonName = j.Left.Left.Name, CreatedByUser = j.Left.Right.Name, ModifiedByUser = j.Right.Name });
                .Select(j => new { PersonName = p.Name, CreatedByUser = cu.Name, ModifiedByUser = mu.Name });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT \"t1\".\"Name\" AS \"PersonName\", \"t3\".\"Name\" AS \"CreatedByUser\", \"t2\".\"Name\" AS \"ModifiedByUser\" FROM \"person\" AS \"t1\" "
                + "INNER JOIN \"Users\" AS \"t2\" ON \"t1\".\"ModifiedByUserId\" = \"t2\".\"Id\" "
                + "INNER JOIN \"Users\" AS \"t3\" ON \"t1\".\"CreatedByUserId\" = \"t3\".\"Id\" "
                + "WHERE \"t1\".\"Age\" > @p0"
                , sql);
            Assert.IsTrue(sql.Contains("WHERE"));
            Assert.IsTrue(sql.Contains("Age"));
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




    }
}
