using System;
using System.Data;
using System.Data.Common;

namespace DLinq
{
    /// <summary>
    /// Encapsulates an IDbTransaction instance and implements System.Data.IDbTransaction.
    /// Allows custom commit and rollback delegates to be invoked on transaction actions.
    /// </summary>
    public class Transaction : DbTransaction, IDbTransaction, IDisposable
    {
        private readonly IDbTransaction _innerTransaction;
        private readonly Action? _onCommit;
        private readonly Action? _onRollback;
        private readonly Action? _onDispose;

        protected internal Transaction(IDbTransaction innerTransaction, Action? onCommit = null, Action? onRollback = null, Action? onDispose = null)
        {
            _innerTransaction = innerTransaction;
            _onCommit = onCommit;
            _onRollback = onRollback;
            _onDispose = onDispose;
        }

        internal IDbTransaction InnerTransaction => _innerTransaction;

        public override void Commit()
        {
            _innerTransaction.Commit();
            _onCommit?.Invoke();
        }

        public override void Rollback()
        {
            _innerTransaction.Rollback();
            _onRollback?.Invoke();
        }

        public new void Dispose()
        {
            _onDispose?.Invoke();
            _innerTransaction.Dispose();
        }

        public virtual new IDbConnection Connection => _innerTransaction.Connection!;
        protected override DbConnection DbConnection => throw new NotImplementedException("DbConnection is not implemented. use 'Connection' instead");
        public override IsolationLevel IsolationLevel => _innerTransaction.IsolationLevel;
    }
}
