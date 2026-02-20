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
using System.Text.Json;

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
        [Table("person")]
        private class PersonPet
        {
            [Key]
            [Column("Id")]
            [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
            public int PersonId { get; set; }
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
            public string FirstName { get; set; }
            public string LastName { get; set; }
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
        public void Select_All_Projection()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Customer>(provider).Select(c => new { FullName = c.FirstName + " " + c.LastName, DepartmentId = 7 });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine($"{sql}\r\n{JsonSerializer.Serialize(parameters)}");
            Assert.AreEqual("SELECT ((\"t1\".\"FirstName\" + @p0) + \"t1\".\"LastName\") AS \"FullName\", @p1 AS \"DepartmentId\" FROM \"Customer\" AS \"t1\"", sql);
        }


        [TestMethod]
        public void Where()
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
        public void Where_Contains()
        {
            var ids = new[] { 1, 2, 3 };
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Where(x => ids.Contains(x.Id));
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.IsTrue(sql.Contains("WHERE"));
            Assert.IsTrue(sql.Contains("Id"));
            Assert.AreEqual("SELECT * FROM \"Person\" AS \"t1\"\r\nWHERE \"t1\".\"Id\" IN (1, 2, 3)", sql);
        }

        [TestMethod]
        public void Where_NotContains()
        {
            var ids = new[] { 1, 2, 3 };
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Where(x => !ids.Contains(x.Id));
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.IsTrue(sql.Contains("WHERE"));   
            Assert.IsTrue(sql.Contains("Id"));
            Assert.AreEqual("SELECT * FROM \"Person\" AS \"t1\"\r\nWHERE \"t1\".\"Id\" NOT IN (1, 2, 3)", sql);
        }

        [TestMethod]
        public void OrderBy()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).OrderBy(x => x.Name);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.IsTrue(sql.Contains("SELECT"));
        }

        [TestMethod]
        public void SkipTake()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Skip(5).Take(10);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.IsTrue(sql.Contains("SELECT"));
        }

        [TestMethod]
        public void ToInsertSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider);
            var (sql, parameters) = query.ToInsertSql(new Person { Name = "Test", Age = 20 });
            Console.WriteLine(sql);
            Assert.IsTrue(sql.StartsWith("INSERT INTO"));
        }

        [TestMethod]
        public void ToUpdateSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider);
            var (sql, parameters) = query.ToUpdateSql(new Person { Id = 1, Name = "Test", Age = 21 });
            Console.WriteLine(sql);
            Assert.IsTrue(sql.StartsWith("UPDATE"));
        }

        [TestMethod]
        public void ToDeleteSql()
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
        public void Join()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .Join<Pet>((Person, pet) => Person.Id == pet.OwnerId)
                .Select<Person,Pet>((p,pt) => new { PersonName = p.Name, PetName = pt.Name });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT \"t1\".\"Name\" AS \"PersonName\", \"t2\".\"Name\" AS \"PetName\" FROM \"Person\" AS \"t1\" "
                +"INNER JOIN \"Pet\" AS \"t2\" ON \"t1\".\"Id\" = \"t2\".\"OwnerId\"", sql);
        }

        [TestMethod]
        public void Join_WithMultipleConditions()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId && pet.Name != null)
                .Select<Person, Pet>((p, pt) => new { PersonName = p.Name, PetName = pt.Name });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT \"t1\".\"Name\" AS \"PersonName\", \"t2\".\"Name\" AS \"PetName\" FROM \"Person\" AS \"t1\" "
                +"INNER JOIN \"Pet\" AS \"t2\" ON (\"t1\".\"Id\" = \"t2\".\"OwnerId\") AND (\"t2\".\"Name\" IS NOT NULL)"
                , sql);
        }

        [TestMethod]
        public void Join_ChainedWithWhereAndSelect()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId)
                .Where(x => x.Age > 18)
                .Select<Person, Pet>((p, pt) => new { PersonName = p.Name, PetName = pt.Name });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT \"t1\".\"Name\" AS \"PersonName\", \"t2\".\"Name\" AS \"PetName\" FROM \"Person\" AS \"t1\" "
                + "INNER JOIN \"Pet\" AS \"t2\" ON \"t1\".\"Id\" = \"t2\".\"OwnerId\"\r\n"
                + "WHERE \"t1\".\"Age\" > @p0"
                , sql);
            Assert.IsTrue(sql.Contains("WHERE"));
            Assert.IsTrue(sql.Contains("Age"));
        }

        [TestMethod]
        public void Join_MultipleOfSameTable()
        {
            var minAge = 18;
            var provider = GetProvider();
            var query = new SqlQuery<Person3>(provider)
                .Where(p => p.Age > minAge)
                .Join<CreatedByUser>((person, user) => person.CreatedByUserId == user.Id)
                .Join<ModifiedByUser>((person, user) => person.ModifiedByUserId == user.Id)
                .Select<Person3, CreatedByUser, ModifiedByUser>((p,c,m) => new { PersonName = p.Name, CreatedByUser = c.Name, ModifiedByUser = m.Name });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT \"t1\".\"Name\" AS \"PersonName\", \"t2\".\"Name\" AS \"CreatedByUser\", \"t3\".\"Name\" AS \"ModifiedByUser\" FROM \"person\" AS \"t1\" "
                +"INNER JOIN \"Users\" AS \"t2\" ON \"t1\".\"CreatedByUserId\" = \"t2\".\"Id\" "
                + "INNER JOIN \"Users\" AS \"t3\" ON \"t1\".\"ModifiedByUserId\" = \"t3\".\"Id\"\r\n"
                + "WHERE \"t1\".\"Age\" > @p0"
                , sql);
            Assert.IsTrue(sql.Contains("WHERE"));
            Assert.IsTrue(sql.Contains("Age"));
        }

        [TestMethod]
        public void Join_MultipleWithSurrogates()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person3>(provider)
                .Join<Person3, ModifiedByUser>((person, modifiedByUser) => person.ModifiedByUserId == modifiedByUser.Id)
                .Join<Person3, CreatedByUser>((person, createdByUser) => person.CreatedByUserId == createdByUser.Id)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId)
                .Where((person) => person.Age > 18)
                .Select<Person3, Pet, CreatedByUser, ModifiedByUser>((person, pet, cu, mu) => new { PersonName = person.Name, PetName = pet.Name, CreatedByUser = cu.Name, ModifiedByUser = mu.Name });

            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT \"t1\".\"Name\" AS \"PersonName\", \"t4\".\"Name\" AS \"PetName\", \"t3\".\"Name\" AS \"CreatedByUser\", \"t2\".\"Name\" AS \"ModifiedByUser\" "
                +"FROM \"person\" AS \"t1\" "
                +"INNER JOIN \"Users\" AS \"t2\" ON \"t1\".\"ModifiedByUserId\" = \"t2\".\"Id\" "
                +"INNER JOIN \"Users\" AS \"t3\" ON \"t1\".\"CreatedByUserId\" = \"t3\".\"Id\" "
                + "INNER JOIN \"Pet\" AS \"t4\" ON \"t1\".\"Id\" = \"t4\".\"OwnerId\"\r\n"
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
