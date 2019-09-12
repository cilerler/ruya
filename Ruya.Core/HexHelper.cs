using System;
using System.Linq;

namespace Ruya.Core
{
    public static class HexHelper
    {
        public static string ToHex(this byte[] byteArray)
        {
            return BitConverter.ToString(byteArray);
        }

        /// <summary>
        /// Converts hexadecimal string to byte array
        /// </summary>
        /// <param name="hex">Must be in double digits e.g. 01 instead of 1</param>
        /// <returns></returns>
        public static byte[] HexToByte(string hex)
        {
            if (hex == null)
            {
                throw new ArgumentNullException(nameof(hex));
            }
            const string hexSeparator = "-";
            string value = hex.Replace(hexSeparator, string.Empty);
            return Enumerable.Range(0, value.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(value.Substring(x, 2), 16))
                             .ToArray();
        }
    }
}
