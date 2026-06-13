using Microsoft.VisualStudio.TestTools.UnitTesting;
using DLinq;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System;
using Moq;
using System.Data;

namespace DLinqTests
{
    [TestClass]
    public class DLinqConnectionTests
    {
        private class SingleKeyEntity
        {
            [Key]
            public int Id { get; set; }
            public string Name { get; set; }
        }

        private class CompositeKeyEntity
        {
            [Key]
            public int Id { get; set; }
            [Key]
            public string SSN { get; set; }
            public string Name { get; set; }
        }

        private class NoKeyEntity
        {
            public int Id { get; set; }
        }

        private Mock<IDapperProvider> mockDapperProvider = new Mock<IDapperProvider>();

        private DLinqConnection GetTestConnection()
        {
            var mockConn = new Mock<IDbConnection>();
            var mockDialect = new Mock<PostgresDialect> { CallBase = true };
            
            return new DLinqConnection(mockConn.Object, mockDialect.Object, mockDapperProvider.Object);
        }

        [TestMethod]
        public void GetById_SingleKey_ReturnsEntityOrNull()
        {
            // Setup the global mock DapperProvider to return the expected entity
            mockDapperProvider.Setup(dc => dc.QuerySingleOrDefault<SingleKeyEntity>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<IDbTransaction>()))
                .Returns(new SingleKeyEntity { Id = 42, Name = "Test" });

            var conn = GetTestConnection();
            var result = conn.GetById<SingleKeyEntity, int>(42);

            Assert.IsNotNull(result);
            Assert.AreEqual(42, result.Id);
            Assert.AreEqual("Test", result.Name);
        }

        [TestMethod]
        public void GetById_CompositeKey_ReturnsEntityOrNull()
        {
            // Setup the global mock DapperProvider to return the expected entity
            mockDapperProvider.Setup(dc => dc.QuerySingleOrDefault<CompositeKeyEntity>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<IDbTransaction>()))
                .Returns(new CompositeKeyEntity { Id = 1, SSN = "abc", Name = "TestName" });

            var conn = GetTestConnection();
            var result = conn.GetById<CompositeKeyEntity>(new { Id = 1, SSN = "abc" });

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Id);
            Assert.AreEqual("abc", result.SSN);
            Assert.AreEqual("TestName", result.Name);
        }

        [TestMethod]
        public void GetById_SingleKey_ThrowsIfMultipleKeys()
        {
            var conn = GetTestConnection();
            Assert.Throws<InvalidOperationException>(() => conn.GetById<CompositeKeyEntity, int>(1));
        }

        [TestMethod]
        public void GetById_CompositeKey_ThrowsIfMissingProperty()
        {
            var conn = GetTestConnection();
            Assert.Throws<ArgumentException>(() => conn.GetById<CompositeKeyEntity>(new { Id = 1 }));
        }

        [TestMethod]
        public void GetById_ThrowsIfNoKey()
        {
            var conn = GetTestConnection();
            Assert.Throws<InvalidOperationException>(() => conn.GetById<NoKeyEntity, int>(1));
        }

        [TestMethod]
        public void Insert_SelectAfterMutation_ReturnsEntity()
        {
            var mockConn = new Mock<IDbConnection>();
            var mockDialect = new Mock<PostgresDialect> { CallBase = true };
            var mockDapperProvider = new Mock<IDapperProvider>();
            var entity = new SingleKeyEntity { Id = 1, Name = "Test" };
            mockDapperProvider.Setup(d => d.QuerySingleOrDefault<SingleKeyEntity>(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<IDbTransaction>())).Returns(entity);
            var conn = new DLinqConnection(mockConn.Object, mockDialect.Object, mockDapperProvider.Object);
            var options = new DLinq.InsertOptions { SelectAfterMutation = true };
            var result = conn.Insert(entity, options);
            Assert.AreEqual(entity, result);
        }

        [TestMethod]
        public void Insert_WithDefaultSchema_SqlIncludesSchema()
        {
            var mockConn = new Mock<IDbConnection>();
            var mockDapperProvider = new Mock<IDapperProvider>();
            var entity = new SingleKeyEntity { Id = 1, Name = "Test" };
            string? capturedSql = null;
            mockDapperProvider
                .Setup(d => d.QuerySingleOrDefault<SingleKeyEntity>(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<IDbTransaction>()))
                .Callback<string, object, IDbTransaction>((sql, _, __) => capturedSql = sql)
                .Returns(entity);
            var conn = new DLinqConnection(mockConn.Object, new PostgresDialect(), mockDapperProvider.Object);
            var options = new InsertOptions { DefaultSchema = "abc", SelectAfterMutation = true };

            conn.Insert(entity, options);

            Assert.IsNotNull(capturedSql);
            Assert.IsTrue(capturedSql.Contains("\"abc\"."), $"Expected schema \"abc\" in SQL but got: {capturedSql}");
        }

        [TestMethod]
        public void Update_SelectAfterMutation_ReturnsEntity()
        {
            var mockConn = new Mock<IDbConnection>();
            var mockDialect = new Mock<PostgresDialect> { CallBase = true };
            var mockDapperProvider = new Mock<IDapperProvider>();
            var entity = new SingleKeyEntity { Id = 2, Name = "Updated" };
            mockDapperProvider.Setup(d => d.QuerySingleOrDefault<SingleKeyEntity>(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<IDbTransaction>())).Returns(entity);
            var conn = new DLinqConnection(mockConn.Object, mockDialect.Object, mockDapperProvider.Object);
            var options = new DLinq.UpdateOptions { SelectAfterMutation = true };
            var result = conn.Update(entity, options);
            Assert.AreEqual(entity, result);
        }

        [TestMethod]
        public void Update_SelectAfterMutation_WithPredicate_ReturnsEntity()
        {
            var mockConn = new Mock<IDbConnection>();
            var mockDialect = new Mock<PostgresDialect> { CallBase = true };
            var mockDapperProvider = new Mock<IDapperProvider>();
            var entity = new SingleKeyEntity { Id = 2, Name = "Updated" };
            mockDapperProvider.Setup(d => d.QuerySingleOrDefault<SingleKeyEntity>(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<IDbTransaction>())).Returns(entity);
            var conn = new DLinqConnection(mockConn.Object, mockDialect.Object, mockDapperProvider.Object);
            var options = new DLinq.UpdateOptions { SelectAfterMutation = true };
            var result = conn.Update<SingleKeyEntity>(entity, _ => _.Name.StartsWith("X"), options);
            Assert.AreEqual(entity, result);
        }

        [TestMethod]
        public void Update_WithDefaultSchema_SqlIncludesSchema()
        {
            var mockConn = new Mock<IDbConnection>();
            var mockDapperProvider = new Mock<IDapperProvider>();
            var entity = new SingleKeyEntity { Id = 2, Name = "Updated" };
            string? capturedSql = null;
            mockDapperProvider
                .Setup(d => d.QuerySingleOrDefault<SingleKeyEntity>(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<IDbTransaction>()))
                .Callback<string, object, IDbTransaction>((sql, _, __) => capturedSql = sql)
                .Returns(entity);
            var conn = new DLinqConnection(mockConn.Object, new PostgresDialect(), mockDapperProvider.Object);
            var options = new UpdateOptions { DefaultSchema = "abc", SelectAfterMutation = true };

            conn.Update(entity, options);

            Assert.IsNotNull(capturedSql);
            Assert.IsTrue(capturedSql.Contains("\"abc\"."), $"Expected schema \"abc\" in SQL but got: {capturedSql}");
        }

        [TestMethod]
        public void GetById_WithSchema_SqlIncludesSchema()
        {
            var mockConn = new Mock<IDbConnection>();
            var mockDapperProvider = new Mock<IDapperProvider>();
            var entity = new SingleKeyEntity { Id = 1, Name = "Test" };
            string? capturedSql = null;
            mockDapperProvider
                .Setup(d => d.QuerySingleOrDefault<SingleKeyEntity>(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<IDbTransaction>()))
                .Callback<string, object, IDbTransaction>((sql, _, __) => capturedSql = sql)
                .Returns(entity);
            var conn = new DLinqConnection(mockConn.Object, new PostgresDialect(), mockDapperProvider.Object);
            var options = new QueryOptions { Schema = "xyz" };

            conn.GetById<SingleKeyEntity, int>(1, options);

            Assert.IsNotNull(capturedSql);
            Assert.IsTrue(capturedSql.Contains("\"xyz\"."), $"Expected schema \"xyz\" in SQL but got: {capturedSql}");
        }

        [TestMethod]
        public void Delete_ExecutesDapper()
        {
            var mockConn = new Mock<IDbConnection>();
            var mockDialect = new Mock<PostgresDialect> { CallBase = true };
            var mockDapperProvider = new Mock<IDapperProvider>();
            mockDapperProvider.Setup(d => d.Execute(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<IDbTransaction>())).Returns(1);
            var conn = new DLinqConnection(mockConn.Object, mockDialect.Object, mockDapperProvider.Object);
            var result = conn.Delete<SingleKeyEntity>(x => x.Id == 1);
            Assert.AreEqual(1, result);
        }

        [TestMethod]
        public void Transaction_Depth_IncrementsAndDecrements()
        {
            var mockConn = new Mock<IDbConnection>();
            var mockDialect = new Mock<PostgresDialect> { CallBase = true };
            var mockDapperProvider = new Mock<IDapperProvider>();
            mockConn.Setup(c => c.BeginTransaction()).Returns(new TransactionWrapper(new Mock<IDbTransaction>().Object));
            var conn = new DLinqConnection(mockConn.Object, mockDialect.Object, mockDapperProvider.Object);
            var trans = conn.BeginTransaction();
            Assert.IsNotNull(trans);
            conn.Commit();
            // After commit, TransactionDepth should be 0
            Assert.AreEqual(0, conn.TransactionDepth);
        }

        [TestMethod]
        public void Transaction_Rollback_ResetsDepth()
        {
            var mockConn = new Mock<IDbConnection>();
            var mockDialect = new Mock<PostgresDialect> { CallBase = true };
            var mockDapperProvider = new Mock<IDapperProvider>();
            mockConn.Setup(c => c.BeginTransaction()).Returns(new TransactionWrapper(new Mock<IDbTransaction>().Object));
            var conn = new DLinqConnection(mockConn.Object, mockDialect.Object, mockDapperProvider.Object);
            var trans = conn.BeginTransaction();
            Assert.IsNotNull(trans);
            conn.Rollback();
            // After rollback, TransactionDepth should be 0
            Assert.AreEqual(0, conn.TransactionDepth);
        }

        [TestMethod]
        public void Dispose_DisposesConnectionAndTransaction()
        {
            var mockConn = new Mock<IDbConnection>();
            var mockDialect = new Mock<PostgresDialect> { CallBase = true };
            var mockDapperProvider = new Mock<IDapperProvider>();
            mockConn.Setup(c => c.BeginTransaction()).Returns(new TransactionWrapper(new Mock<IDbTransaction>().Object));
            var conn = new DLinqConnection(mockConn.Object, mockDialect.Object, mockDapperProvider.Object);
            var trans = conn.BeginTransaction();
            conn.Dispose();
            mockConn.Verify(c => c.Dispose(), Times.Once);
        }

        [TestMethod]
        public void Query_WithPredicate_ReturnsExpectedEntities()
        {
            // Arrange
            var expected = new[]
            {
                new SingleKeyEntity { Id = 1, Name = "A" },
                new SingleKeyEntity { Id = 2, Name = "B" }
            };
            mockDapperProvider.Setup(dc => dc.Query<SingleKeyEntity>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<IDbTransaction>()))
                .Returns(expected);

            var conn = GetTestConnection();

            // Act
            var result = conn.Query<SingleKeyEntity>(x => x.Id > 0).ToList();

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(1, result[0].Id);
            Assert.AreEqual("A", result[0].Name);
            Assert.AreEqual(2, result[1].Id);
            Assert.AreEqual("B", result[1].Name);
        }

        [TestMethod]
        public void Query_WithVariablePredicate_ReturnsExpectedEntities()
        {
            // Arrange
            var expected = new[]
            {
                new SingleKeyEntity { Id = 1, Name = "A" },
                new SingleKeyEntity { Id = 2, Name = "B" }
            };
            mockDapperProvider.Setup(dc => dc.Query<SingleKeyEntity>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<IDbTransaction>()))
                .Returns(expected);

            var conn = GetTestConnection();
            var minId = 0;

            // Act
            var result = conn.Query<SingleKeyEntity>(x => x.Id > minId).ToList();

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(1, result[0].Id);
            Assert.AreEqual("A", result[0].Name);
            Assert.AreEqual(2, result[1].Id);
            Assert.AreEqual("B", result[1].Name);
        }

        [TestMethod]
        public void Query_WithObjectPropertyPredicate_ReturnsExpectedEntities()
        {
            // Arrange
            var expected = new[]
            {
                new SingleKeyEntity { Id = 1, Name = "A" },
                new SingleKeyEntity { Id = 2, Name = "B" }
            };
            mockDapperProvider.Setup(dc => dc.Query<SingleKeyEntity>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<IDbTransaction>()))
                .Returns(expected);

            var conn = GetTestConnection();
            var entity = new { minId = 0 };

            // Act
            var result = conn.Query<SingleKeyEntity>(x => x.Id > entity.minId).ToList();

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(1, result[0].Id);
            Assert.AreEqual("A", result[0].Name);
            Assert.AreEqual(2, result[1].Id);
            Assert.AreEqual("B", result[1].Name);
        }

        private class PersonPetJoin { public Person person { get; set; } public Pet pet { get; set; } }

        private class Person
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }
        private class Pet
        {
            public int Id { get; set; }
            public int OwnerId { get; set; }
            public string Name { get; set; }
        }
        private class AnonymousResult
        {
            public string PersonName { get; set; }
            public string PetName { get; set; }
        }

        // This will trigger the NotSupportedException in the constructor
        private class UnsupportedDialect : DummyDialect
        {
            
        }
    }
}
