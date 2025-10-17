using Microsoft.VisualStudio.TestTools.UnitTesting;
using DLinq;

namespace DLinqTests
{
    [TestClass]
    public class OptionsTests
    {
        [TestMethod]
        public void Default_SelectAfterMutation_IsFalse()
        {
            var options = new Options();
            Assert.IsFalse(options.SelectAfterMutation);
        }
    }
}
