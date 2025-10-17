using Microsoft.VisualStudio.TestTools.UnitTesting;
using DLinq;
using System.Collections.Generic;

namespace DLinqTests
{
    [TestClass]
    public class SqlAstNodesTests
    {
        [TestMethod]
        public void SqlSelectNode_Defaults()
        {
            var node = new SqlSelectNode();
            Assert.IsNotNull(node.Columns);
            Assert.IsNotNull(node.PrimaryKeys);
            Assert.IsNotNull(node.OrderBy);
            Assert.IsNotNull(node.Joins);
        }

        [TestMethod]
        public void SqlJoinNode_Properties()
        {
            var join = new SqlJoinNode { Table = "T", LeftColumn = "A", RightColumn = "B", JoinType = "INNER" };
            Assert.AreEqual("T", join.Table);
            Assert.AreEqual("A", join.LeftColumn);
            Assert.AreEqual("B", join.RightColumn);
            Assert.AreEqual("INNER", join.JoinType);
        }

        [TestMethod]
        public void SqlFunctionSource_Properties()
        {
            var fn = new SqlFunctionSource { FunctionName = "fn", Arguments = new List<object> { 1, "x" } };
            Assert.AreEqual("fn", fn.FunctionName);
            Assert.AreEqual(2, fn.Arguments.Count);
        }

        [TestMethod]
        public void SqlWhereNode_Properties()
        {
            var where = new SqlWhereNode { Column = "Col", Operator = "=", Value = 42, IsSubQuery = true };
            Assert.AreEqual("Col", where.Column);
            Assert.AreEqual("=", where.Operator);
            Assert.AreEqual(42, where.Value);
            Assert.IsTrue(where.IsSubQuery);
        }
    }
}
