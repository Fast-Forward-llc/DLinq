using Microsoft.VisualStudio.TestTools.UnitTesting;
using DLinq;
using System.Linq;
using System;

namespace DLinqTests
{
    [TestClass]
    public class SqlQueryExtensionsTests
    {
        [TestMethod]
        public void ToSql_ThrowsOnUnsupportedProvider()
        {
            var queryable = new DummyQueryable();
            Assert.ThrowsException<NotSupportedException>(() => SqlQueryExtensions.ToSql(queryable));
        }

        private class DummyQueryable : IQueryable
        {
            public Type ElementType => typeof(object);
            public System.Linq.Expressions.Expression Expression => System.Linq.Expressions.Expression.Constant(this);
            public IQueryProvider Provider => new DummyProvider();
            public System.Collections.IEnumerator GetEnumerator() => throw new NotImplementedException();
        }
        private class DummyProvider : IQueryProvider
        {
            public IQueryable CreateQuery(System.Linq.Expressions.Expression expression) => null;
            public IQueryable<TElement> CreateQuery<TElement>(System.Linq.Expressions.Expression expression) => null;
            public object Execute(System.Linq.Expressions.Expression expression) => null;
            public TResult Execute<TResult>(System.Linq.Expressions.Expression expression) => default;
        }
    }
}
