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

        [Table("DummyTable")]
        private class TestEntity
        {
            [Key]
            public int Id { get; set; }
            public string Name { get; set; }
        }

        [TestMethod]
        public void Join_MethodChaining_LinqSyntax_GeneratesSql()
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
        public void Join_LinqSyntax_WithWhere_GeneratesSql()
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
        public void ToSql_InnerJoin_LinqStyle_GeneratesCorrectSql()
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
            StringAssert.Contains(sql, "\"t2\".\"DepartmentId\" = \"t1\".\"Id\"", "SQL should contain the correct ON clause for the join.");
            StringAssert.Contains(sql, "JOIN \"Department\" AS \"t1\"", "SQL should contain JOIN keyword.");
            StringAssert.Contains(sql, "FROM \"Employee\" AS \"t2\"", "SQL should reference Employee table.");
            StringAssert.Contains(sql, "\"Department\" AS \"t1\"", "SQL should reference Department table.");
            StringAssert.Contains(sql, "\"t2\".\"Id\", \"t2\".\"DepartmentId\", \"t2\".\"Name\"", "SQL should reference columns");
        }

        [TestMethod]
        public void Join_LinqStyleWithoutInnerQuery_GeneratesSql()
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
        public void Join_LinqStyleWithoutInnerQuery_WithWhere_GeneratesSql()
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

        [TestMethod]
        public void Join_OptimizedSyntax_GeneratesSql()
        {
            var provider = GetProvider();
            var people = new SqlQuery<Person>(provider);
            // Use the new overload that returns Pair<Person, Pet>
            var joined = people.Join<Pet, int>(
                person => person.Id,
                pet => pet.OwnerId
            );
            var (sql, parameters) = joined.ToSql();
            Console.WriteLine(sql);
            Assert.IsTrue(sql.Contains("JOIN") || sql.Contains("join") || sql.Contains("FROM"));
            Assert.IsTrue(sql.Contains("Person") || sql.Contains("person"));
            Assert.IsTrue(sql.Contains("Pet") || sql.Contains("pet"));
        }

        [TestMethod]
        public void Join_OptimizedSyntax_WithWhereAndSelect_GeneratesSql()
        {
            var provider = GetProvider();
            var people = new SqlQuery<Person>(provider);
            var personPet = people.Join<Pet, int>(
                person => person.Id,
                pet => pet.OwnerId
            )
            .Where(x => x.Left.Name == "Alice")
            .Select(x => new PersonPet { PersonName = x.Left.Name, PetName = x.Right.Name });
            var (sql, parameters) = personPet.ToSql();
            Console.WriteLine(sql);
            Assert.IsTrue(sql.Contains("WHERE"));
            Assert.IsTrue(sql.Contains("PersonName"));
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual("Alice", paramDict["p0"]);
        }

        [TestMethod]
        public void MultipleJoins_OptimizedSyntax_GeneratesCorrectSql()
        {
            var provider = GetProvider();
            var orders = new SqlQuery<Order>(provider);
            // First join: Order + Customer => Pair<Order, Customer>
            var orderCustomer = orders.Join<Customer, int>(
                o => o.CustomerId,
                c => c.Id
            );
            // Second join: Pair<Order, Customer> + Product => Pair<Pair<Order, Customer>, Product>
            var orderCustomerProduct = orderCustomer.Join<Product, int>(
                oc => oc.Left.ProductId,
                p => p.Id
            );
            // Select projection from the nested pair
            var query = orderCustomerProduct.Select(x => new OrderInfo
            {
                CustomerName = x.Left.Right.Name, // x.Left is Pair<Order, Customer>, x.Left.Right is Customer
                ProductName = x.Right.Name
            });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.IsTrue(sql.Contains("JOIN", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(sql.Contains("Order", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(sql.Contains("Customer", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(sql.Contains("Product", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(sql.Contains("CustomerName", StringComparison.OrdinalIgnoreCase) || sql.Contains("ProductName", StringComparison.OrdinalIgnoreCase));
        }


    }
}
