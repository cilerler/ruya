using System;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace Ruya.Security
{
    public static class StringHelper
    {
        public static string ToMD5(this string input, Encoding encoding)
        {
            if (encoding == null)
            {
                encoding = Encoding.Default;
            }
            string output;
            using (var md5 = new MD5CryptoServiceProvider())
            {
                byte[] originalBytes = encoding.GetBytes(input);
                byte[] modifiedBytes = md5.ComputeHash(originalBytes);
                output = BitConverter.ToString(modifiedBytes);
            }
            return output;
        }
    }
}