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
            Assert.ThrowsException<InvalidOperationException>(() => conn.GetById<CompositeKeyEntity, int>(1));
        }

        [TestMethod]
        public void GetById_CompositeKey_ThrowsIfMissingProperty()
        {
            var conn = GetTestConnection();
            Assert.ThrowsException<ArgumentException>(() => conn.GetById<CompositeKeyEntity>(new { Id = 1 }));
        }

        [TestMethod]
        public void GetById_ThrowsIfNoKey()
        {
            var conn = GetTestConnection();
            Assert.ThrowsException<InvalidOperationException>(() => conn.GetById<NoKeyEntity, int>(1));
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
            var options = new DLinq.Options { SelectAfterMutation = true };
            var result = conn.Insert(entity, options);
            Assert.AreEqual(entity, result);
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
            var options = new DLinq.Options { SelectAfterMutation = true };
            var result = conn.Update(entity, options);
            Assert.AreEqual(entity, result);
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

        // This will trigger the NotSupportedException in the constructor
        private class UnsupportedDialect : ISqlDialect
        {
            public string FormatTable(string tableName) => tableName;
            public string FormatColumn(string columnName) => columnName;
            public string ParameterPlaceholder(int index) => "@p" + index;
            public string SelectStatement(SqlSelectNode ast, System.Collections.Generic.List<object> parameters) => "SELECT";
            public string InsertStatement(string tableName, List<string> columns, List<string> paramNames, DLinq.Options options)
            {
                return $"INSERT INTO {tableName}";
            }
            public string UpdateStatement(string tableName, object setValues, object whereValues, DLinq.Options options, System.Collections.Generic.List<(string colName, object value)> primaryKeys) => "UPDATE";
            public string DeleteStatement(string tableName, object whereValues) => "DELETE";
            public string IdentityValueExpression(string tableName, string columnName)
            {
                return $"<identity>";
            }
        }
    }
}
