using System.Data;
using DLinq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DLinqTests
{
    [TestClass]
    public class TransactionTests
    {
        [TestMethod]
        public void Commit_InvokesInnerAndDelegate()
        {
            var mockTrans = new Mock<IDbTransaction>();
            bool committed = false;
            var tx = new TransactionWrapper(mockTrans.Object, onCommit: () => committed = true);
            tx.Commit();
            mockTrans.Verify(t => t.Commit(), Times.Once);
            Assert.IsTrue(committed);
        }

        [TestMethod]
        public void Rollback_InvokesInnerAndDelegate()
        {
            var mockTrans = new Mock<IDbTransaction>();
            bool rolledBack = false;
            var tx = new TransactionWrapper(mockTrans.Object, onRollback: () => rolledBack = true);
            tx.Rollback();
            mockTrans.Verify(t => t.Rollback(), Times.Once);
            Assert.IsTrue(rolledBack);
        }

        [TestMethod]
        public void Dispose_InvokesInnerAndDelegate()
        {
            var mockTrans = new Mock<IDbTransaction>();
            bool disposed = false;
            var tx = new TransactionWrapper(mockTrans.Object, onDispose: () => disposed = true);
            tx.Dispose();
            mockTrans.Verify(t => t.Dispose(), Times.Once);
            Assert.IsTrue(disposed);
        }

        [TestMethod]
        public void Properties_ForwardedToInner()
        {
            var mockConn = new Mock<IDbConnection>();
            var mockTrans = new Mock<IDbTransaction>();
            mockTrans.Setup(t => t.Connection).Returns(mockConn.Object);
            mockTrans.Setup(t => t.IsolationLevel).Returns(IsolationLevel.Serializable);
            var tx = new TransactionWrapper(mockTrans.Object);
            Assert.AreEqual(mockConn.Object, tx.Connection);
            Assert.AreEqual(IsolationLevel.Serializable, tx.IsolationLevel);
        }
    }

    public class TransactionWrapper: Transaction
    {
        public TransactionWrapper(IDbTransaction innerTransaction, Action onCommit = null, Action onRollback = null, Action onDispose = null)
            : base(innerTransaction, onCommit, onRollback, onDispose)
        {
        }
}
}
