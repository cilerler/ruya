using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Core.Tests
{
    [TestClass]
    public class DateTimeTest
    {
        [TestMethod]
        public void GetRandomDateTime()
        {
            // Arrange
            var expectedValues = new List<DateTime>();
            var actualValues = new List<DateTime>();
            const int maxTestItem = 10;

            // Act
            for (int counter = 0; counter < maxTestItem; counter++)
            {
                DateTime value = DateTimeHelper.GetRandomDateTime(DateTime.UtcNow.AddDays(-counter), DateTime.UtcNow.AddDays(+counter));
                expectedValues.Add(value);
            }
            for (int counter = 0; counter < maxTestItem; counter++)
            {
                DateTime value = DateTimeHelper.GetRandomDateTime(DateTime.UtcNow.AddDays(-counter), DateTime.UtcNow.AddDays(+counter));
                actualValues.Add(value);
            }

            // Assert
            CollectionAssert.AreNotEqual(expectedValues, actualValues);
        }

        [TestMethod]
        public void CombineDateTime()
        {
            // Arrange
            var dateValue = new DateTime(2015, 02, 10, 08, 15, 21);
            var timeValue = new DateTime(2015, 02, 09, 16, 30, 59);
            var expectedValue = new DateTime(2015, 02, 10, 16, 30, 59);
            
            // Act
            var actualValue = DateTimeHelper.CombineDateTime(dateValue, timeValue);

            // Assert
            Assert.AreEqual(expectedValue, actualValue);
        }
    }
}
