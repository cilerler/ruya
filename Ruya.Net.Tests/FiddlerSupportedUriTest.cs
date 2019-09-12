using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Net;

namespace Ruya.Net.Tests
{
    [TestClass]
    public class FiddlerSupportedUriTest
    {
        [TestMethod]
        public void UriWithoutFiddler()
        {
            // Arrange
            var value = new FiddlerSupportedUri();
            const string expectedValue = "http://localhost/";

            // Act
            string actualValue = value.ToString();

            // Assert
            Assert.IsFalse(value.IsFiddlerSafe);
            Assert.AreEqual(expectedValue, actualValue);
        }

        [TestMethod]
        public void UriWithFiddler()
        {
            // Arrange
            var value = new FiddlerSupportedUri().AddFiddlerSupport();
            const string expectedValue = "http://localhost.fiddler/";

            // Act
            string actualValue = value.ToString();

            // Assert
            Assert.IsTrue(value.IsFiddlerSafe);
            Assert.AreEqual(expectedValue, actualValue);
        }

        [TestMethod]
        public void UriWithFiddlerWithPort()
        {
            // Arrange
            var value = new FiddlerSupportedUri(8080).AddFiddlerSupport();
            const string expectedValue = "http://localhost.fiddler:8080/";

            // Act
            string actualValue = value.ToString();

            // Assert
            Assert.IsTrue(value.IsFiddlerSafe);
            Assert.AreEqual(expectedValue, actualValue);
        }

        [TestMethod]
        public void UriWithFiddlerWithUri()
        {
            // Arrange
            var uri = new Uri("http://microsoft.com:8181");
            var value = new FiddlerSupportedUri(uri).AddFiddlerSupport();
            const string expectedValue = "http://microsoft.com:8181/";

            // Act
            string actualValue = value.ToString();

            // Assert
            Assert.IsFalse(value.IsFiddlerSafe);
            Assert.AreEqual(expectedValue, actualValue);
        }

        [TestMethod]
        public void UriWithFiddlerWithUriLocalhost()
        {
            // Arrange
            var uri = new Uri("http://localhost:8282");
            var value = new FiddlerSupportedUri(uri).AddFiddlerSupport();
            const string expectedValue = "http://localhost.fiddler:8282/";

            // Act
            string actualValue = value.ToString();

            // Assert
            Assert.IsTrue(value.IsFiddlerSafe);
            Assert.AreEqual(expectedValue, actualValue);
        }
    }
}
