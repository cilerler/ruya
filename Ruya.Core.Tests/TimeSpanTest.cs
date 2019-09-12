using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Core;

namespace Ruya.Core.Tests
{
    [TestClass]
    public class TimeSpanTest
    {
        [TestMethod]
        public void GetIntervalInvalid()
        {
            // Arrange
            const string value = "0:1?0";
            TimeSpan expectedValue = new TimeSpan();

            // Act
            TimeSpan actualValue = TimeSpanHelper.GetInterval(value);

            // Assert
            Assert.AreEqual(expectedValue, actualValue);
        }

        [TestMethod]
        public void GetIntervalValid()
        {
            // Arrange
            const string value = "0:1:0";
            TimeSpan expectedValue = new TimeSpan(0, 1, 0);

            // Act
            TimeSpan actualValue = TimeSpanHelper.GetInterval(value);

            // Assert
            Assert.AreEqual(expectedValue, actualValue);
        }
    }
}
