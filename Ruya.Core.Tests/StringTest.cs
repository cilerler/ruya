using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Core.Tests
{
    [TestClass]
    public class StringTest
    {
        [TestMethod]
        public void SplitCRLF()
        {
            // Arrange
            string[] lines = { string.Empty, "Core",string.Empty, "Test", string.Empty, "Strings", string.Empty};
            string value = string.Join(ControlChars.CarriageReturnLineFeed, lines);
            var expectedValue = new List<string>
                                {
                                    lines[1],
                                    lines[3],
                                    lines[5]
                                };

            // Act
            List<string> actualValue = value.SplitCRLF(System.StringSplitOptions.RemoveEmptyEntries).ToList();

            // Assert
            CollectionAssert.AreEqual(expectedValue, actualValue);
        }

        [TestMethod]
        public void GenerateRandomText()
        {
            // Arrange
            var expectedValues = new List<string>();
            var actualValues = new List<string>();
            const int maxTestItem = 10;

            // Act
            for (int counter = 0; counter < maxTestItem; counter++)
            {
                string value = StringHelper.GenerateRandomText(6, StringFeatures.All);
                expectedValues.Add(value);
            }
            for (int counter = 0; counter < maxTestItem; counter++)
            {
                string value = StringHelper.GenerateRandomText(6, StringFeatures.SymbolEasy | StringFeatures.SymbolDifficult | StringFeatures.Number | StringFeatures.LetterUpper | StringFeatures.LetterLower);
                actualValues.Add(value);
            }

            // Assert
            CollectionAssert.AreNotEqual(expectedValues, actualValues);
        }        
    }
}