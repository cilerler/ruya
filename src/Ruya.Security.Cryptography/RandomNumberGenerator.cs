using System.Text;

namespace Ruya.Security.Cryptography
{
    public class RandomGenerator
    {
        public static string Generate(int maxSize)
        {
            const string characters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
            char[] chars = characters.ToCharArray();
            System.Security.Cryptography.RandomNumberGenerator randomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator.Create();

            var data = new byte[maxSize];
            randomNumberGenerator.GetBytes(data);
            var output = new StringBuilder(maxSize);
            foreach (byte item in data)
            {
                output.Append(chars[item % chars.Length]);
            }
            return output.ToString();
        }
    }
}
