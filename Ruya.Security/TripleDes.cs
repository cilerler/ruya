using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Ruya.Security
{
    public sealed class TripleDes : IDisposable
    {
        private readonly TripleDESCryptoServiceProvider _tdesProvider = new TripleDESCryptoServiceProvider();

        public KeyValuePair<byte[], byte[]> Secret => new KeyValuePair<byte[], byte[]>(_tdesProvider.Key, _tdesProvider.IV);

        /// <summary>
        /// Instantiates TripleDes and generates Key and IV
        /// </summary>
        public TripleDes()
        {
            _tdesProvider.GenerateKey();
            _tdesProvider.GenerateIV();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="secret"></param>
        public TripleDes(KeyValuePair<byte[], byte[]> secret)
        {
            _tdesProvider.Key = secret.Key;
            _tdesProvider.IV = secret.Value;
        }

        public TripleDes(byte[] key, byte[] iv)
        {
            _tdesProvider.Key = key;
            _tdesProvider.IV = iv;
        }

        public string Encrypt(string value)
        {
            // Declare a UTF8Encoding object so we may use the GetByte
            // method to transform the plainText into a Byte array.
            var utf8Encoder = new UTF8Encoding();
            byte[] inputInBytes = utf8Encoder.GetBytes(value);

            // The ICryptTransform interface uses the TripleDES
            // crypt provider along with encryption key and init vector
            // information 
            ICryptoTransform cryptoTransform = _tdesProvider.CreateEncryptor();



            // All cryptographic functions need a stream to output the
            // encrypted information.  Here we declare a memory stream
            // for this purpose.
            byte[] result;
            
            MemoryStream encryptedStream = null;
            CryptoStream cryptoStream = null;
            try
            {
                encryptedStream = new MemoryStream();
                cryptoStream = new CryptoStream(encryptedStream, cryptoTransform, CryptoStreamMode.Write);                
                
                    // Write the encrypted information to the stream.  Flush the information
                    // when done to ensure everything is out of the buffer.
                    cryptoStream.Write(inputInBytes, 0, inputInBytes.Length);
                    cryptoStream.FlushFinalBlock();

                    // Read the stream back into a Byte array and return it to the calling
                    // method.
                    result = encryptedStream.ToArray();
                
            }
            finally
            {
                if (cryptoStream != null)
                {
                    cryptoStream.Dispose();
                }
                else
                {
                    encryptedStream?.Dispose();
                }
            }

            // Convert the encrypted stream to a printable string.
            return Convert.ToBase64String(result);
        }

        
        public string Decrypt(string encryptedValue)
        {
            // Convert the encrypted text string to a byte array.
            byte[] value = Convert.FromBase64String(encryptedValue);

            // As before we must provide the encryption/decryption key along with
            // the init vector.
            ICryptoTransform cryptoTransform = _tdesProvider.CreateDecryptor();

            // Provide a memory stream to decrypt information into
            byte[] result;
            MemoryStream decryptedStream = null;
            CryptoStream cryptoStream = null;

            try
            {
                decryptedStream = new MemoryStream();
                cryptoStream = new CryptoStream(decryptedStream, cryptoTransform, CryptoStreamMode.Write);
                cryptoStream.Write(value, 0, value.Length);
                cryptoStream.FlushFinalBlock();

                // Read the memory stream and convert it back into a string
                result = decryptedStream.ToArray();
            }
            finally
            {
                if (cryptoStream != null)
                {
                    cryptoStream.Dispose();
                }
                else
                {
                    decryptedStream?.Dispose();
                }
            }

            var output = new UTF8Encoding();
            return output.GetString(result);
        }

        public void Dispose()
        {
            (_tdesProvider as IDisposable).Dispose();
        }
    }
}