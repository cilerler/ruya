using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Core.Tests
{

    [TestClass]
    public class EnumerationTest
    {

        [Flags]
        enum Days : byte
        {
            None = 0,

            [System.ComponentModel.Description("Sunday")]
            Sun = 1,

            [System.ComponentModel.Description("Monday")]
            Mon = 2,

            [System.ComponentModel.Description("Tuesday")]
            Tue = 4,

            [System.ComponentModel.Description("Wednesday")]
            Wed = 8,

            [System.ComponentModel.Description("Thursday")]
            Thu = 16,

            [System.ComponentModel.Description("Friday")]
            Fri = 32,

            [System.ComponentModel.Description("Saturday")]
            Sat = 64,

            All = Sun | Mon | Tue | Wed | Thu | Thu | Fri | Sat
        };

        [TestMethod]
        public void GetValue()
        {
            // Arrange
            Enum value = Days.Fri | Days.Sat;
            const byte expectedValue = 96;

            // Act
            object actualValue = value.GetValue();

            // Assert
            Assert.AreEqual(expectedValue, actualValue);
        }

        [TestMethod]
        public void ToEnumerableSpecificType()
        {
            // Arrange
            Enum value = (Days)96;
            var expectedValue = new List<Days>
                                {
                                    Days.Sat,
                                    Days.Fri
                                };

            // Act
            List<Days> actualValue = value.ToEnumerable().Cast<Days>().ToList();

            // Assert
            CollectionAssert.AreEquivalent(expectedValue, actualValue);
        }

        [TestMethod]
        public void ToEnumerable()
        {
            // Arrange
            Enum value = (Days) 96;
            var expectedValue = new List<Enum>
                                {
                                    Days.Sat,
                                    Days.Fri
                                };

            // Act
            List<Enum> actualValue = value.ToEnumerable().ToList();
            
            // Assert
            CollectionAssert.AreEquivalent(expectedValue, actualValue);
        }
        
        [TestMethod]
        public void ToEnumerable_DefaultValueOnly()
        {
            // Arrange
            const long defaultEnumValue = 0;
            Enum value = (Days) defaultEnumValue;
            var expectedValue = new List<Enum>
                                {
                                    Days.None
                                };
            // Act
            List<Enum> actualValue = value.ToEnumerable().ToList();

            // Assert
            CollectionAssert.AreEquivalent(expectedValue, actualValue);
        }

        [TestMethod]
        public void ToEnumerable_WithDefaultValue()
        {
            // Arrange
            Enum value = Days.Fri | Days.Sat | Days.None;
            var expectedValue = new List<Enum>
                                {
                                    Days.Sat,
                                    Days.Fri
                                };
            // Act
            List<Enum> actualValue = value.ToEnumerable().ToList();

            // Assert
            CollectionAssert.AreEquivalent(expectedValue, actualValue);
        }

        [TestMethod]
        public void GetBitSum()
        {
            // Arrange
            Type value = typeof (Days);            
            const long expectedValue = 254;

            // Act
            long actualValue = value.GetBitSum();

            // Assert
            Assert.AreEqual(expectedValue, actualValue);
        }

        [TestMethod]
        public void GetSum()
        {
            // Arrange
            var value = new List<Days>
                                {
                                    Days.Sat,
                                    Days.Fri
                                };
            const long expectedValue = 96;

            // Act
            long actualValue = EnumHelper.GetSum(value);

            // Assert
            Assert.AreEqual(expectedValue, actualValue);
        }

        [TestMethod]
        public void GetDescription()
        {
            // Arrange
            Enum value = Days.Fri;
            const string expectedValue = "Friday";

            // Act
            string actualValue = value.GetDescription();
            
            // Assert
            Assert.AreEqual(expectedValue, actualValue);
        }

        [TestMethod]
        public void GetDescription_NotAvailable()
        {
            // Arrange
            Enum value = Days.All;

            // Act
            string actualValue = value.GetDescription();

            // Assert
            Assert.IsNull(actualValue);
        }

        [TestMethod]
        public void GetEnumFromDescription()
        {
            // Arrange
            const string value = "Friday";            
            const Days expectedValue = Days.Fri;

            // Act
            var actualValue = EnumHelper.GetEnumFromDescription<Days>(value, false);

            // Assert
            Assert.AreEqual(expectedValue, actualValue);
        }

        [TestMethod]
        public void GetRandomEnumItem()
        {
            // Arrange
            var expectedValues = new List<Days>();
            var actualValues = new List<Days>();
            const int maxTestItem = 10;
            
            // Act
            for (int counter = 0; counter < maxTestItem; counter++)
            {
                Days value = EnumHelper.GetRandomEnumItem<Days>();
                expectedValues.Add(value);
            }
            for (int counter = 0; counter < maxTestItem; counter++)
            {
                Days value = EnumHelper.GetRandomEnumItem<Days>();
                actualValues.Add(value);
            }

            // Assert
            CollectionAssert.AreNotEqual(expectedValues, actualValues);
        }
    }
}