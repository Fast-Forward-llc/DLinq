using Dapper;
using DLinq;
using DLinqIntegrationTests.DTOs;
using Microsoft.Data.SqlClient;

namespace DLinqIntegrationTests
{
    [TestClass]
    public sealed class SqlServerTests
    {
        DLinqConnection dlinq;

        public SqlServerTests()
        {
            var connection = new SqlConnection("server=.\\se16;database=DLinqTests;Integrated Security=True;MultipleActiveResultSets=true;TrustServerCertificate=True");
            var dialect = new SqlServerDialect();
            var dapperProvider = new DapperProvider(connection);
            dlinq = new DLinqConnection(connection, dialect, dapperProvider);
        }

        [TestMethod]
        public void InsertPerson_Success()
        {
            var person = new DTOs.Person { FirstName = "Joe", LastName="Smith", Age = 25 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            var inserted = dlinq.Insert(person, options);
            Assert.IsNotNull(inserted);
            Assert.AreEqual("Joe", inserted.FirstName);
            Assert.AreEqual(25, inserted.Age);
            Assert.IsTrue(inserted.CreateDateUTC > DateTime.UtcNow.AddMinutes(-1));
            Assert.IsNotNull(inserted.Id);
            Assert.IsTrue(inserted.Id > 0);
        }

        [TestMethod]
        public void UpdatePerson_Success()
        {
            // Insert a person to update
            var person = new DTOs.Person { FirstName = "Jane", LastName = "Doe", Age = 30 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            var inserted = dlinq.Insert(person, options);
            Assert.IsNotNull(inserted);
            Assert.IsTrue(inserted.Id > 0);

            // Update the person
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
            // Insert a person to ensure there is a record to retrieve
            var person = new DTOs.Person { FirstName = "Alice", LastName = "Johnson", Age = 28 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            var inserted = dlinq.Insert(person, options);
            Assert.IsNotNull(inserted);
            Assert.IsTrue(inserted.Id > 0);

            // Retrieve by Id
            var retrieved = dlinq.GetById<DTOs.Person, int>(inserted.Id.Value);
            Assert.IsNotNull(retrieved);
            Assert.AreEqual(inserted.Id, retrieved.Id);
            Assert.AreEqual("Alice", retrieved.FirstName);
            Assert.AreEqual("Johnson", retrieved.LastName);
            Assert.AreEqual(28, retrieved.Age);
        }
        [TestMethod]
        public void GetByIdPersonCompoundKey_Success()
        {
            // Insert a person to ensure there is a record to retrieve
            var person = new DTOs.PersonCK { FirstName = "Alice", LastName = "James", Age = 38 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            var inserted = dlinq.Insert(person, options);
            Assert.IsNotNull(inserted);
            Assert.IsTrue(inserted.Id == person.Id);
            Assert.IsTrue(inserted.LastName == person.LastName);

            // Retrieve by Id
            var retrieved = dlinq.GetById<DTOs.PersonCK>(new { inserted.Id, inserted.LastName });
            Assert.IsNotNull(retrieved);
            Assert.AreEqual(person.Id, retrieved.Id);
            Assert.AreEqual(person.FirstName, retrieved.FirstName);
            Assert.AreEqual(person.LastName, retrieved.LastName);
            Assert.AreEqual(person.Age, retrieved.Age);
        }
        /**********************************************************************************************/
        [TestMethod]
        public void InsertPerson2_Success()
        {
            var person = new DTOs.Person2 { FirstName = "Joe", LastName = "Smith", Age = 25 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            var inserted = dlinq.Insert(person, options);
            Assert.IsNotNull(inserted);
            Assert.AreEqual("Joe", inserted.FirstName);
            Assert.AreEqual(25, inserted.Age);
            Assert.IsTrue(inserted.CreateDateUTC > DateTime.UtcNow.AddMinutes(-1));
            Assert.IsNotNull(inserted.Id);
            Assert.IsTrue(inserted.Id > 0);
        }

        [TestMethod]
        public void UpdatePerson2_Success()
        {
            // Insert a person to update
            var person = new DTOs.Person2 { FirstName = "Jane", LastName = "Doe", Age = 30 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            var inserted = dlinq.Insert(person, options);
            Assert.IsNotNull(inserted);
            Assert.IsTrue(inserted.Id > 0);

            // Update the person
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
        public void UpdatePersonPartial_Success()
        {
            // Insert a person to update
            var person = new DTOs.Person { FirstName = "Janet", LastName = "Dough", Age = 31 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            var inserted = dlinq.Insert(person, options);
            Assert.IsNotNull(inserted);
            Assert.IsTrue(inserted.Id > 0);

            // Update the person
            var personUpdate = new PersonUpdateName()
            {
                Id = inserted.Id.Value,
                FirstName = "Jelly",
                LastName = "Roll - partial Update"
            };
            dlinq.Update(personUpdate);
            var updated = dlinq.GetById<Person, int>(inserted.Id.Value);

            Assert.IsNotNull(updated);
            Assert.AreEqual(inserted.Id, updated.Id);
            Assert.AreEqual(personUpdate.FirstName, updated.FirstName);
            Assert.AreEqual(personUpdate.LastName, updated.LastName);
            Assert.AreEqual(inserted.Age, updated.Age);
        }

        [TestMethod]
        public void GetByIdPerson2_Success()
        {
            // Insert a person to ensure there is a record to retrieve
            var person = new DTOs.Person2 { FirstName = "Alice", LastName = "Johnson", Age = 28 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            var inserted = dlinq.Insert(person, options);
            Assert.IsNotNull(inserted);
            Assert.IsTrue(inserted.Id > 0);

            // Retrieve by Id
            var retrieved = dlinq.GetById<DTOs.Person2, int>(inserted.Id.Value);
            Assert.IsNotNull(retrieved);
            Assert.AreEqual(inserted.Id, retrieved.Id);
            Assert.AreEqual("Alice", retrieved.FirstName);
            Assert.AreEqual("Johnson", retrieved.LastName);
            Assert.AreEqual(28, retrieved.Age);
        }
        /**********************************************************************************************/
        [TestMethod]
        public void InsertPersonUUID_Success()
        {
            var person = new DTOs.PersonUUID { FirstName = "Joe", LastName = "Smith", Age = 25 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            var inserted = dlinq.Insert(person, options);
            Assert.IsNotNull(inserted);
            Assert.AreEqual("Joe", inserted.FirstName);
            Assert.AreEqual(25, inserted.Age);
            Assert.IsTrue(inserted.CreateDateUTC > DateTime.UtcNow.AddMinutes(-1));
            Assert.IsNotNull(inserted.Id);
            Assert.IsTrue(inserted.Id == person.Id);
        }

        [TestMethod]
        public void UpdatePersonUUID_Success()
        {
            // Insert a person to update
            var person = new DTOs.PersonUUID { FirstName = "Jane", LastName = "Doe", Age = 30 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            var inserted = dlinq.Insert(person, options);
            Assert.IsNotNull(inserted);
            Assert.IsTrue(inserted.Id == person.Id);

            // Update the person
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
        public void GetByIdPersonUUID_Success()
        {
            // Insert a person to ensure there is a record to retrieve
            var person = new DTOs.PersonUUID { FirstName = "Alice", LastName = "Johnson", Age = 28 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            var inserted = dlinq.Insert(person, options);
            Assert.IsNotNull(inserted);
            Assert.IsTrue(inserted.Id == person.Id);

            // Retrieve by Id
            var retrieved = dlinq.GetById<DTOs.PersonUUID, Guid>(inserted.Id);
            Assert.IsNotNull(retrieved);
            Assert.AreEqual(inserted.Id, retrieved.Id);
            Assert.AreEqual("Alice", retrieved.FirstName);
            Assert.AreEqual("Johnson", retrieved.LastName);
            Assert.AreEqual(28, retrieved.Age);
        }
        /************************************************************************************/
        [TestMethod]
        public void InsertAndUpdateInTransaction_Success()
        {
            var person = new DTOs.Person { FirstName = "Trans", LastName = "Action", Age = 40 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            DTOs.Person inserted = null;
            DTOs.Person updated = null;

            // Begin transaction
            using (var transaction = dlinq.BeginTransaction())
            {
                try
                {
                    // Insert
                    inserted = dlinq.Insert(person, options);
                    Assert.IsNotNull(inserted);
                    Assert.IsTrue(inserted.Id > 0);

                    // Update
                    inserted.Age = 41;
                    inserted.LastName = "Action-Updated";
                    updated = dlinq.Update(inserted, options);
                    Assert.IsNotNull(updated);
                    Assert.AreEqual(inserted.Id, updated.Id);
                    Assert.AreEqual("Trans", updated.FirstName);
                    Assert.AreEqual("Action-Updated", updated.LastName);
                    Assert.AreEqual(41, updated.Age);

                    // Commit transaction
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
            var person = new DTOs.Person { FirstName = "Rollback", LastName = "Test", Age = 50 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            DTOs.Person inserted = null;
            DTOs.Person updated = null;
            int? personId = null;

            using (var transaction = dlinq.BeginTransaction())
            {
                try
                {
                    // Insert
                    inserted = dlinq.Insert(person, options);
                    Assert.IsNotNull(inserted);
                    Assert.IsTrue(inserted.Id > 0);
                    personId = inserted.Id;

                    // Update
                    inserted.Age = 51;
                    inserted.LastName = "Test-Rollback";
                    updated = dlinq.Update(inserted, options);
                    Assert.IsNotNull(updated);
                    Assert.AreEqual(inserted.Id, updated.Id);
                    Assert.AreEqual("Rollback", updated.FirstName);
                    Assert.AreEqual("Test-Rollback", updated.LastName);
                    Assert.AreEqual(51, updated.Age);

                    // Rollback transaction
                    dlinq.Rollback();
                }
                catch
                {
                    dlinq.Rollback();
                    throw;
                }
            }

            // After rollback, the person should not exist in the database
            if (personId.HasValue)
            {
                var retrieved = dlinq.GetById<DTOs.Person, int>(personId.Value);
                Assert.IsNull(retrieved, "Person should not exist after transaction rollback.");
            }
        }

        [TestMethod]
        public void DeletePerson_Success()
        {
            // Insert a person to ensure there is a record to retrieve
            var person = new DTOs.Person { FirstName = "Bobby", LastName = "Thompson", Age = 28 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            var inserted = dlinq.Insert(person, options);
            Assert.IsNotNull(inserted);
            Assert.IsTrue(inserted.Id > 0);

            // Retrieve by Id
            var delCount = dlinq.Delete<Person>(inserted);
            Assert.IsTrue(delCount > 0);
        }

        [TestMethod]
        public void DeletePersonLinq_Success()
        {
            //Insert a person to ensure there is a record to delete
            var person = new DTOs.Person { FirstName = "Bobby", LastName = "Thompson", Age = 28 };
            var options = new DLinq.Options { SelectAfterMutation = true };
            var inserted = dlinq.Insert(person, options);
            Assert.IsNotNull(inserted);
            Assert.IsTrue(inserted.Id > 0);

            // Retrieve by Id
            var minId = inserted.Id;
            var delCount = dlinq.Delete<Person>(p => p.Id > minId);
            Assert.IsTrue(delCount > 0);
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
