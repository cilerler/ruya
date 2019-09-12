using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Diagnostics.Tests
{
    [TestClass]
    public class StackTraceTest
    {
        [TestMethod]
        public void GetMethodFromStack()
        {
            // Arrange            
            string expectedValue = MethodBase.GetCurrentMethod()
                                             .Name;

            // Act
            MethodBase value = StackTraceHelper.GetMethod(1);
            string actualValue = value.Name;

            // Assert
            Assert.AreEqual(expectedValue, actualValue);
        }

        [TestMethod]
        public void GetMethodFromStack_UsingStackTraceCaller()
        {
            // Arrange            
            string expectedValue = MethodBase.GetCurrentMethod()
                                             .Name;

            // Act
            MethodBase value = StackTraceHelper.GetMethod(StackTraceCaller.This);
            string actualValue = value.Name;

            // Assert
            Assert.AreEqual(expectedValue, actualValue);
        }

#if NET45_OR_GREATER

        [TestMethod]
        public void GetCallerInformation()
        {
            // Arrange         
            string projectDirectory = Directory.GetParent(Directory.GetCurrentDirectory())?.Parent?.FullName;
            if (string.IsNullOrEmpty(projectDirectory))
            {
                throw new NotImplementedException();
            }
            const int callerLineNumber = 68;
            string callerFilePath = projectDirectory + @"\StackTraceTest.cs";
            string callerMemberName = MethodBase.GetCurrentMethod().Name;
            var expectedValue = new Dictionary<string, string>
                                {
                                    {
                                        nameof(callerLineNumber), callerLineNumber.ToString()
                                    },
                                    {
                                        nameof(callerFilePath), callerFilePath
                                    },
                                    {
                                        nameof(callerMemberName), callerMemberName
                                    }
                                };

            // Act
            Dictionary<string, string> actualValue = StackTraceHelper.GetCallerInformation();

            // Assert
            CollectionAssert.AreEquivalent(expectedValue, actualValue);
        }
#endif
    }
}