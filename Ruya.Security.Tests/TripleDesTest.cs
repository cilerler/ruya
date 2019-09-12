// <copyright file="TripleDesTest.cs" company="Cengiz Ilerler">Copyright ©  2015</copyright>

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Pex.Framework;
using Microsoft.Pex.Framework.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Security.Tests
{
    [TestClass]
    public class TripleDesTest2
    {
        [TestMethod]
        public void EncryptDescryptTest()
        {
            // Arrange
            var tripleDes = new TripleDes();
            string expectedValue = MethodBase.GetCurrentMethod()
                                             .Name;
            // Act
            string value = tripleDes.Encrypt(expectedValue);
            string actualValue = tripleDes.Decrypt(value);

            // Assert
            Assert.AreEqual(expectedValue, actualValue);
            Assert.AreNotEqual(expectedValue, value);
        }
    }

    /// <summary>This class contains parameterized unit tests for TripleDes</summary>
    [PexClass(typeof (TripleDes)), PexAllowedExceptionFromTypeUnderTest(typeof (InvalidOperationException)), PexAllowedExceptionFromTypeUnderTest(typeof (ArgumentException), AcceptExceptionSubtypes = true), TestClass]
    public partial class TripleDesTest
    {
        /// <summary>Test stub for .ctor()</summary>
        [PexMethod]
        public TripleDes ConstructorTest()
        {
            TripleDes target = new TripleDes();
            return target;
            // TODO: add assertions to method TripleDesTest.ConstructorTest()
        }

        /// <summary>Test stub for .ctor(KeyValuePair`2&lt;Byte[],Byte[]&gt;)</summary>
        [PexMethod]
        public TripleDes ConstructorTest01(KeyValuePair<byte[], byte[]> secret)
        {
            TripleDes target = new TripleDes(secret);
            return target;
            // TODO: add assertions to method TripleDesTest.ConstructorTest01(KeyValuePair`2<Byte[],Byte[]>)
        }

        /// <summary>Test stub for .ctor(Byte[], Byte[])</summary>
        [PexMethod]
        public TripleDes ConstructorTest02(byte[] key, byte[] iv)
        {
            TripleDes target = new TripleDes(key, iv);
            return target;
            // TODO: add assertions to method TripleDesTest.ConstructorTest02(Byte[], Byte[])
        }

        /// <summary>Test stub for Decrypt(String)</summary>
        [PexMethod]
        public string DecryptTest([PexAssumeUnderTest] TripleDes target, string encryptedValue)
        {
            string result = target.Decrypt(encryptedValue);
            return result;
            // TODO: add assertions to method TripleDesTest.DecryptTest(TripleDes, String)
        }

        /// <summary>Test stub for Dispose()</summary>
        [PexMethod]
        public void DisposeTest([PexAssumeUnderTest] TripleDes target)
        {
            target.Dispose();
            // TODO: add assertions to method TripleDesTest.DisposeTest(TripleDes)
        }

        /// <summary>Test stub for Encrypt(String)</summary>
        [PexMethod]
        public string EncryptTest([PexAssumeUnderTest] TripleDes target, string value)
        {
            string result = target.Encrypt(value);
            return result;
            // TODO: add assertions to method TripleDesTest.EncryptTest(TripleDes, String)
        }

        /// <summary>Test stub for get_Secret()</summary>
        [PexMethod]
        public KeyValuePair<byte[], byte[]> SecretGetTest([PexAssumeUnderTest] TripleDes target)
        {
            KeyValuePair<byte[], byte[]> result = target.Secret;
            return result;
            // TODO: add assertions to method TripleDesTest.SecretGetTest(TripleDes)
        }
    }
}