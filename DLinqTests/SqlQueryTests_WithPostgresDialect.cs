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

        private class PersonPetJoin
        {
            public Person Person { get; set; }
            public Pet Pet { get; set; }
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
            var query = new SqlQuery<TestEntity>(new QueryProvider(new PostgresDialect()));
            var (sql, parameters) = query.ToInsertSql(new TestEntity { Id = 1, Name = "abc" });
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
            StringAssert.Contains(sql, "DELETE FROM \"DummyTable\"");
            StringAssert.Contains(sql, "WHERE \"Id\" = @Id");
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

        [TestMethod]
        public void Join_MethodChaining_GeneratesSql()
        {
            var provider = GetProvider();
            var people = new SqlQuery<Person>(provider);
            var pets = new SqlQuery<Pet>(provider);
            var joined = people.Join(
                pets,
                person => person.Id,
                pet => pet.OwnerId,
                (person, pet) => new PersonPet { PersonName = person.Name, PetName = pet.Name }
            );
            var (sql, parameters) = joined.ToSql();
            Console.WriteLine(sql);
            Assert.IsTrue(sql.Contains("JOIN") || sql.Contains("join") || sql.Contains("FROM"));
            // Should reference both Person and Pet columns
            Assert.IsTrue(sql.Contains("Person") || sql.Contains("person"));
            Assert.IsTrue(sql.Contains("Pet") || sql.Contains("pet"));
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
            Console.WriteLine(sql);
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

        [TestMethod]
        public void ToSql_InnerJoin_GeneratesCorrectSql()
        {
            var provider = GetProvider();
            var employees = new SqlQuery<Employee>(provider);
            var departments = new SqlQuery<Department>(provider);

            var joined = employees.Join(
                departments,
                emp => emp.DepartmentId,
                dept => dept.Id,
                (emp, dept) => new EmployeeDepartment { EmployeeName = emp.Name, DepartmentName = dept.Name }
            );

            var (sql, parameters) = joined.ToSql();

            // Print for debug if needed
            Console.WriteLine(sql);

            // Validate SQL contains fully qualified ON clause with both key columns
            StringAssert.Contains(sql, "\"Employee\".\"DepartmentId\" = \"Department\".\"Id\"", "SQL should contain the correct ON clause for the join.");
            StringAssert.Contains(sql, "JOIN", "SQL should contain JOIN keyword.");
            StringAssert.Contains(sql, "\"Employee\"", "SQL should reference Employee table.");
            StringAssert.Contains(sql, "\"Department\"", "SQL should reference Department table.");
        }

        [TestMethod]
        public void Join_OverloadWithoutInnerQuery_GeneratesSql()
        {
            var provider = GetProvider();
            var people = new SqlQuery<Person>(provider);
            // Use the new overload that does not require explicit inner SqlQuery parameter
            var joined = people.Join<Pet, int, PersonPetJoin>(
                person => person.Id,
                pet => pet.OwnerId,
                (person, pet) => new PersonPetJoin { Person = person, Pet = pet }
            ).Select( _ => new PersonPet() { PersonName = _.Person.Name, PetName = _.Pet.Name });
            var (sql, parameters) = joined.ToSql();
            Console.WriteLine(sql);
            Assert.IsTrue(sql.Contains("JOIN") || sql.Contains("join") || sql.Contains("FROM"));
            Assert.IsTrue(sql.Contains("Person") || sql.Contains("person"));
            Assert.IsTrue(sql.Contains("Pet") || sql.Contains("pet"));
        }

        [TestMethod]
        public void Join_OverloadWithoutInnerQuery_WithWhere_GeneratesSql()
        {
            var targetName = "Alice";
            var provider = GetProvider();
            var people = new SqlQuery<Person>(provider);
            var joined = people.Join<Pet, int, PersonPetJoin>(
                person => person.Id,
                pet => pet.OwnerId,
                (person, pet) => new PersonPetJoin { Person = person, Pet = pet }
            ).Where(x => x.Person.Name == targetName).Select(x => new PersonPet { PersonName = x.Person.Name, PetName = x.Pet.Name });
            var (sql, parameters) = joined.ToSql();
            Console.WriteLine(sql);
            Assert.IsTrue(sql.Contains("WHERE"));
            Assert.IsTrue(sql.Contains("PersonName"));
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual("Alice", paramDict["p0"]);
        }

        
    }
}
