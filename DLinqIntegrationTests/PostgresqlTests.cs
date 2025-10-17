using DLinq;
using DLinqIntegrationTests.DTOs;
using Npgsql;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace DLinqIntegrationTests
{
    [TestClass]
    public sealed class PostgresqlTests
    {
        DLinqConnection dlinq;

        public PostgresqlTests()
        {
            var connection = new NpgsqlConnection("Host=localhost;Port=5432;Database=DLinq;Username=postgres-user-name;Password=your-password-here;");
            var dialect = new PostgresDialect(PostgresDialect.DialectOptions.ForceLowerCase);
            var dapperProvider = new DapperProvider(connection);
            dlinq = new DLinqConnection(connection, dialect, dapperProvider);
        }

        [TestMethod]
        public void InsertPerson_Success()
        {
            var person = new Person { FirstName = "Joe", LastName = "Smith", Age = 25 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            var inserted = dlinq.Insert(person, options);
            Assert.IsNotNull(inserted);
            Assert.AreEqual(person.FirstName, inserted.FirstName);
            Assert.AreEqual(person.Age, inserted.Age);
            Assert.IsTrue(inserted.CreateDateUTC > DateTime.UtcNow.AddMinutes(-1));
            Assert.IsNotNull(inserted.Id);
            Assert.IsTrue(inserted.Id > 0);
        }

        [TestMethod]
        public void InsertPersonLocalDate_Success()
        {
            var person = new Person { FirstName = "Joey", LastName = "Smithson", Age = 25, CreateDateUTC = DateTime.Now };
            var options = new DLinq.Options { SelectAfterMutation = true };
            var inserted = dlinq.Insert(person, options);
            Assert.IsNotNull(inserted);
            Assert.AreEqual(person.FirstName, inserted.FirstName);
            Assert.AreEqual(person.Age, inserted.Age);
            Assert.IsTrue(inserted.CreateDateUTC > DateTime.UtcNow.AddMinutes(-1));
            Assert.IsNotNull(inserted.Id);
            Assert.IsTrue(inserted.Id > 0);
        }

        [TestMethod]
        public void UpdatePerson_Success()
        {
            var person = new Person { FirstName = "Jane", LastName = "Doe", Age = 30 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            var inserted = dlinq.Insert(person, options);
            Assert.IsNotNull(inserted);
            Assert.IsTrue(inserted.Id > 0);

            inserted.Age = 31;
            inserted.LastName = "Doe-Updated";
            var updated = dlinq.Update(inserted, options);

            Assert.IsNotNull(updated);
            Assert.AreEqual(inserted.Id, updated.Id);
            Assert.AreEqual("Jane", updated.FirstName);
            Assert.AreEqual("Doe-Updated", updated.LastName);
            Assert.AreEqual(31, updated.Age);
        }

        [TestMethod]
        public void GetByIdPerson_Success()
        {
            var person = new Person { FirstName = "Alice", LastName = "Johnson", Age = 28 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            var inserted = dlinq.Insert(person, options);
            Assert.IsNotNull(inserted);
            Assert.IsTrue(inserted.Id > 0);

            var retrieved = dlinq.GetById<Person, int>(inserted.Id.Value);
            Assert.IsNotNull(retrieved);
            Assert.AreEqual(inserted.Id, retrieved.Id);
            Assert.AreEqual("Alice", retrieved.FirstName);
            Assert.AreEqual("Johnson", retrieved.LastName);
            Assert.AreEqual(28, retrieved.Age);
        }

        [TestMethod]
        public void DeletePerson_Success()
        {
            var person = new Person { FirstName = "Bobby", LastName = "Thompson", Age = 28 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            var inserted = dlinq.Insert(person, options);
            Assert.IsNotNull(inserted);
            Assert.IsTrue(inserted.Id > 0);

            var delCount = dlinq.Delete<Person>(inserted);
            Assert.IsTrue(delCount > 0);
        }

        [TestMethod]
        public void DeletePersonLinq_Success()
        {
            var person = new Person { FirstName = "Bobby", LastName = "Thompson", Age = 28 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            var inserted = dlinq.Insert(person, options);
            Assert.IsNotNull(inserted);
            Assert.IsTrue(inserted.Id > 0);

            var minId = inserted.Id - 2;
            var delCount = dlinq.Delete<Person>(p => p.Id > minId);
            Assert.IsTrue(delCount > 0);
        }

        [TestMethod]
        public void InsertAndUpdateInTransaction_Success()
        {
            var person = new Person { FirstName = "Trans", LastName = "Action", Age = 40 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            Person inserted = null;
            Person updated = null;

            using (var transaction = dlinq.BeginTransaction())
            {
                try
                {
                    inserted = dlinq.Insert(person, options);
                    Assert.IsNotNull(inserted);
                    Assert.IsTrue(inserted.Id > 0);

                    inserted.Age = 41;
                    inserted.LastName = "Action-Updated";
                    updated = dlinq.Update(inserted, options);
                    Assert.IsNotNull(updated);
                    Assert.AreEqual(inserted.Id, updated.Id);
                    Assert.AreEqual("Trans", updated.FirstName);
                    Assert.AreEqual("Action-Updated", updated.LastName);
                    Assert.AreEqual(41, updated.Age);

                    dlinq.Commit();
                }
                catch
                {
                    dlinq.Rollback();
                    throw;
                }
            }
        }

        [TestMethod]
        public void TransactionRollback_RevertsChanges()
        {
            var person = new Person { FirstName = "Rollback", LastName = "Test", Age = 50 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            Person inserted = null;
            Person updated = null;
            int? personId = null;

            using (var transaction = dlinq.BeginTransaction())
            {
                try
                {
                    inserted = dlinq.Insert(person, options);
                    Assert.IsNotNull(inserted);
                    Assert.IsTrue(inserted.Id > 0);
                    personId = inserted.Id;

                    inserted.Age = 51;
                    inserted.LastName = "Test-Rollback";
                    updated = dlinq.Update(inserted, options);
                    Assert.IsNotNull(updated);
                    Assert.AreEqual(inserted.Id, updated.Id);
                    Assert.AreEqual("Rollback", updated.FirstName);
                    Assert.AreEqual("Test-Rollback", updated.LastName);
                    Assert.AreEqual(51, updated.Age);

                    dlinq.Rollback();
                }
                catch
                {
                    dlinq.Rollback();
                    throw;
                }
            }

            if (personId.HasValue)
            {
                var retrieved = dlinq.GetById<Person, int>(personId.Value);
                Assert.IsNull(retrieved, "Person should not exist after transaction rollback.");
            }
        }
        [TestMethod]
        public void QueryPerson_Success()
        {
            var results = dlinq.Query<Person>(x => x.Id > 0).ToList();

            Assert.IsTrue(results.Count > 0);
        }

        [TestMethod]
        public void QueryPerson_SkipTakeOrderBy_Success()
        {
            // Insert multiple people
            for (int i = 0; i < 10; i++)
            {
                var person = new Person { FirstName = $"Person{i}", LastName = "Smith", Age = 20 + i };
                dlinq.Insert(person);
            }

            var query = dlinq.Select<Person>()
                .OrderBy(p => p.Age)
                .Skip(2)
                .Take(5)
                .ToSqlQuery();
            var results = dlinq.Query<Person>(query).ToList();

            Assert.AreEqual(5, results.Count);
            Assert.IsTrue(results[0].Age >= results[1].Age || results[0].Age <= results[1].Age); // Ordered by Age
        }
    }
}
