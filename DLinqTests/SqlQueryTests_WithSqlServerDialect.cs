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

            // Validate SQL contains JOIN and correct ON clause
            Assert.IsTrue(sql.Contains("JOIN", StringComparison.OrdinalIgnoreCase), "SQL should contain JOIN keyword.");
            Assert.IsTrue(sql.Contains("DepartmentId", StringComparison.OrdinalIgnoreCase), "SQL should reference DepartmentId.");
            Assert.IsTrue(sql.Contains("Id", StringComparison.OrdinalIgnoreCase), "SQL should reference Id.");
            Assert.IsTrue(sql.Contains("Employee") || sql.Contains("employee"), "SQL should reference Employee table.");
            Assert.IsTrue(sql.Contains("Department") || sql.Contains("department"), "SQL should reference Department table.");
        }

        [TestMethod]
        public void Join_OverloadWithoutInnerQuery_GeneratesSql()
        {
            var provider = GetProvider();
            var people = new SqlQuery<Person>(provider);
            // Use the new overload that does not require explicit inner SqlQuery parameter
            var joined = people.Join<Pet, int, PersonPet>(
                person => person.Id,
                pet => pet.OwnerId,
                (person, pet) => new PersonPet { PersonName = person.Name, PetName = pet.Name }
            );
            var (sql, parameters) = joined.ToSql();
            Assert.IsTrue(sql.Contains("JOIN") || sql.Contains("join") || sql.Contains("FROM"));
            Assert.IsTrue(sql.Contains("Person") || sql.Contains("person"));
            Assert.IsTrue(sql.Contains("Pet") || sql.Contains("pet"));
        }

        [TestMethod]
        public void Join_OverloadWithoutInnerQuery_WithWhere_GeneratesSql()
        {
            var provider = GetProvider();
            var people = new SqlQuery<Person>(provider);
            var joined = people.Join<Pet, int, PersonPet>(
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

        [TestMethod]
        public void MultipleJoins_GeneratesSql()
        {
            var provider = GetProvider();
            var orders = new SqlQuery<Order>(provider);
            var query = orders
                .Join<Customer, int, OrderCustomer>(
                    o => o.CustomerId,
                    c => c.Id,
                    (o, c) => new OrderCustomer { o = o, c = c }
                )
                .Join<Product, int, OrderCustomerProduct>(
                    oc => oc.o.ProductId,
                    p => p.Id,
                    (oc, p) => new OrderCustomerProduct { oc = oc, p = p }
                )
                .Select(x => new OrderInfo
                {
                    CustomerName = x.oc.c.Name,
                    ProductName = x.p.Name
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
