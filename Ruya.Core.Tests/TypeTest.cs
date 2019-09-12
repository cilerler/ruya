using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Core.Tests
{
    [TestClass]
    public class TypeTest
    {
        [TestMethod]
        public void IsNumeric()
        {
            // Arrange
            object[] values =
            {
                false,
                (byte) 0,
                (sbyte) 0,
                (ushort) 0,
                (uint) 0,
                (ulong) 0,
                (short) 0,
                // ReSharper disable once RedundantCast
                (int)0,
                (long) 0,
                (decimal) 0.00,
                // ReSharper disable once RedundantCast
                (double)0.00,
                (float) 0.00
            };

            // Act
            IEnumerable<bool> actualValues = values.Select(value => value.GetType()
                                                                         .IsNumeric(true));

            // Assert
            Assert.IsFalse(actualValues.Any(av => !av));
        }

        [TestMethod]
        public void IsNumeric_String()
        {
            // Arrange
            object value = "123";

            // Act
            bool actualValue = value.GetType()
                                    .IsNumeric(false);

            // Assert
            Assert.IsFalse(actualValue);
        }

        [TestMethod]
        public void IsNumeric_DateTime()
        {
            // Arrange
            object value = DateTime.UtcNow;

            // Act
            bool actualValue = value.GetType()
                                    .IsNumeric(false);

            // Assert
            Assert.IsFalse(actualValue);
        }

        [TestMethod]
        public void IsNumeric_Boolean()
        {
            // Arrange
            object value = true;

            // Act
            bool actualValue = value.GetType()
                                    .IsNumeric(false);

            // Assert
            Assert.IsFalse(actualValue);
        }

        [TestMethod]
        public void GetTypeFromName()
        {
            // Arrange
            const string value = "Decimal";
            Type expectedValue = typeof (decimal);

            // Act
            Type actualValue = TypeHelper.GetTypeFromName(value, true);

            // Assert
            Assert.AreEqual(expectedValue, actualValue);
        }

        [TestMethod]
        public void GetTypeFromName_NotValid()
        {
            // Arrange
            const string value = "Decimal";

            // Act
            Type actualValue = TypeHelper.GetTypeFromName(value, false);

            // Assert
            Assert.IsNull(actualValue);
        }

        [TestMethod]
        public void SqlTypes()
        {
            // Arrange
            object[] values =
            {
                true,
                ControlChars.Space,
                string.Empty,
                byte.MaxValue,
                //x sbyte.MaxValue,
                short.MaxValue,
                int.MaxValue,
                long.MaxValue,
                ushort.MaxValue,
                uint.MaxValue,
                ulong.MaxValue,
                float.MaxValue,
                double.MaxValue,
                decimal.MaxValue,
                DateTime.MaxValue,
                TimeSpan.MaxValue,
                Guid.NewGuid(),
                new byte[]
                {
                },
                new object()
            };

            string[] expectedValues =
            {
                "bit",
                "nchar",
                "varchar (MAX)",
                "tinyint",
                "smallint",
                "int",
                "bigint",
                "smallint",
                "int",
                "bigint",
                "real",
                "float",
                "numeric",
                "datetime2",
                "time",
                "uniqueidentifier",
                "varbinary",
                "sql_variant"
            };
            Hashtable types = TypeHelper.SqlTypes;

            // Act
            string[] actualValues = values.Select(value => types[value.GetType()] as string)
                                          .ToArray();

            // Assert
            CollectionAssert.AreEqual(expectedValues, actualValues);
        }

        [TestMethod]
        public void SqlTypesModified()
        {
            // Arrange
            object[] values =
            {
                true,
                ControlChars.Space,
                string.Empty,
                byte.MaxValue,
                //x sbyte.MaxValue,
                short.MaxValue,
                int.MaxValue,
                long.MaxValue,
                ushort.MaxValue,
                uint.MaxValue,
                ulong.MaxValue,
                float.MaxValue,
                double.MaxValue,
                decimal.MaxValue,
                DateTime.MaxValue,
                TimeSpan.MaxValue,
                Guid.NewGuid(),
                new byte[]
                {
                },
                new object()
            };

            string[] expectedValues =
            {
                "bit",
                "nchar",
                "varchar ({0})",
                "tinyint",
                "smallint",
                "int",
                "bigint",
                "smallint",
                "int",
                "bigint",
                "real",
                "float",
                "numeric",
                "datetime2",
                "time",
                "uniqueidentifier",
                "varbinary",
                "sql_variant"
            };
            Hashtable types = TypeHelper.SqlTypes;
            types[typeof (string)] = "varchar ({0})";

            // Act
            string[] actualValues = values.Select(value => types[value.GetType()] as string)
                                          .ToArray();

            // Assert
            CollectionAssert.AreEqual(expectedValues, actualValues);
        }
    }
}