using System.Linq;

namespace Ruya.Primitives.Constants;

public static class CharacterSets
{
	public static readonly string LowercaseLetters = string.Concat(Enumerable.Range('a', 26).Select(c => (char)c));
	public static readonly string UppercaseLetters = string.Concat(Enumerable.Range('A', 26).Select(c => (char)c));
	public static readonly string Numeric = string.Concat(Enumerable.Range('0', 10).Select(c => (char)c));
	public static readonly string Alpha = LowercaseLetters + UppercaseLetters;
	public static readonly string Alphanumeric = Alpha + Numeric;
}
