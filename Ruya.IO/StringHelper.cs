using System.IO;
using System.Text;

namespace Ruya.IO
{
    public static class StringHelper
    {
        public static Stream ToStream(this string input, Encoding encoding)
        {
            return input.ToMemoryStream(encoding);
        }

        public static MemoryStream ToMemoryStream(this string input, Encoding encoding)
        {
            if (ReferenceEquals(encoding, null))
            {
                encoding = Encoding.Default;
            }
            var output = new MemoryStream(encoding.GetBytes(input));
            return output;
        }
    }
}