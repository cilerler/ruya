using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Core.Tests
{
    [TestClass]
    public class HexTest
    {
        [TestMethod]
        public void ToHex()
        {
            // Arrange
            byte[] value = { 1, 2, 4, 8, 16, 32 };
            const string expectedValue = "01-02-04-08-10-20";

            // Act
            string actualValue = value.ToHex();

            // Assert
            Assert.AreEqual(expectedValue, actualValue);
        }

        [TestMethod]
        public void HexToByte()
        {
            // Arrange
            const string value = "01-02-04-08-10-20";
            byte[] expectedValue = { 1, 2, 4, 8, 16, 32 };

            // Act
            byte[] actualValue = HexHelper.HexToByte(value);

            // Assert
            CollectionAssert.AreEqual(expectedValue, actualValue);
        }

    }
}
