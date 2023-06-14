using System.Text;

namespace Ruya.Security.Cryptography;

// ReSharper disable once InconsistentNaming
public static class MD5
{
	// ReSharper disable once InconsistentNaming
	public static string CreateMD5(this string value)
	{
		byte[] inputBytes = Encoding.ASCII.GetBytes(value);
		byte[] hashBytes = System.Security.Cryptography.MD5.HashData(inputBytes);

		var stringBuilder = new StringBuilder();
		foreach (byte hashByte in hashBytes)
			stringBuilder.Append(hashByte.ToString("X2"));
		return stringBuilder.ToString();
	}
}
