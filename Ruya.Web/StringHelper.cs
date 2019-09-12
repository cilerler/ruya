using System;
using System.Text;
using System.Web;

namespace Ruya.Web
{
    public static class StringHelper
    {
        public static Uri ToUrlSafeBase64(this string input, Encoding encoding)
        {
            if (ReferenceEquals(encoding, null)) throw new ArgumentNullException(nameof(encoding));
            byte[] encodedBytes = encoding.GetBytes(input);
            return new Uri(HttpServerUtility.UrlTokenEncode(encodedBytes));
        }

        public static Uri FromUrlSafeBase64(this string input, Encoding encoding)
        {
            if (ReferenceEquals(encoding, null)) throw new ArgumentNullException(nameof(encoding));
            byte[] decodedBytes = HttpServerUtility.UrlTokenDecode(input);
            return new Uri(decodedBytes != null
                       ? encoding.GetString(decodedBytes)
                       : string.Empty);
        }
    }
}