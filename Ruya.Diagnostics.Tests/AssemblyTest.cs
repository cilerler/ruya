using System;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Diagnostics.Tests
{
    [TestClass]
    public class AssemblyTest
    {
        [TestMethod]
        public void GetFileVersionInfo()
        {
            // Arrange
            Assembly value = Assembly.GetExecutingAssembly();
            const string expectedValue = "Diagnostics.Test";

            // Act
            string actualValue = value.GetFileVersionInfo().ProductName;

            // Assert
            Assert.IsTrue(actualValue.EndsWith(expectedValue, StringComparison.Ordinal));
        }

        [TestMethod]
        public void GetTitleAttribute()
        {
            // Arrange
            Assembly value = Assembly.GetExecutingAssembly();
            const string expectedValue = "Diagnostics.Test";

            // Act
            string actualValue = value.GetTitleAttribute();

            // Assert
            Assert.IsTrue(actualValue.EndsWith(expectedValue, StringComparison.Ordinal));
        }

        [TestMethod]
        public void GetConfigurationAttribute()
        {
            // Arrange
            Assembly value = Assembly.GetExecutingAssembly();
#if DEBUG
            const string expectedValue = Core.Constants.Debug;
#else
            const string expectedValue = Core.Constants.Release;
#endif

            // Act
            string actualValue = value.GetConfigurationAttribute();

            // Assert
            Assert.AreEqual(expectedValue, actualValue);
        }
    }
}
