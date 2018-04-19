using System.Text;

namespace Ruya.Security.Cryptography
{
    // ReSharper disable once InconsistentNaming
    public class MD5
    {
        public static string Create(string value)
        {
            System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create();
            byte[] inputBytes = Encoding.ASCII.GetBytes(value);
            byte[] hashBytes = md5.ComputeHash(inputBytes);

            var stringBuilder = new StringBuilder();
            foreach (byte hashByte in hashBytes)
            {
                stringBuilder.Append(hashByte.ToString("X2"));
            }
            return stringBuilder.ToString();
        }
    }
}
