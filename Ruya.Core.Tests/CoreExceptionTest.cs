using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Core.Tests
{
    [TestClass]
    public class CoreExceptionTest
    {
        [TestMethod]
        [ExpectedException(typeof (CoreException))]
        public void CoreException()
        {
            throw new CoreException();
        }
    }
}
