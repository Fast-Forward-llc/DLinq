using System;
using System.Data;

namespace DLinq
{
    /// <summary>
    /// Encapsulates an IDbTransaction instance and implements System.Data.IDbTransaction.
    /// Allows custom commit and rollback delegates to be invoked on transaction actions.
    /// </summary>
    public class Transaction : IDbTransaction, IDisposable
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

        public void Commit()
        {
            _innerTransaction.Commit();
            _onCommit?.Invoke();
        }

        public void Rollback()
        {
            _innerTransaction.Rollback();
            _onRollback?.Invoke();
        }

        public void Dispose()
        {
            _onDispose?.Invoke();
            _innerTransaction.Dispose();
        }

        public IDbConnection Connection => _innerTransaction.Connection;
        public IsolationLevel IsolationLevel => _innerTransaction.IsolationLevel;
    }
}
