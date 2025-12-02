using System;

namespace Ruya.Primitives;

/// <summary>
/// Provides constants for ASCII control characters.
/// </summary>
public static class ControlChars
{
	/// <summary>
	///     Chr(8)
	/// </summary>
	public const char BackSpace = '\b';

	/// <summary>
	///     Chr(13)
	/// </summary>
	public const char CarriageReturn = '\r';

	/// <summary>
	///     Chr(10)
	/// </summary>
	public const char LineFeed = '\n';

	/// <summary>
	///     Chr(13) + Chr(10)
	/// </summary>
	public const string CarriageReturnLineFeed = "\r\n";

	/// <summary>
	///     CarriageReturnLineFeed on Windows, LineFeed on Linux
	/// </summary>
	public static readonly string NewLine = Environment.NewLine;

	/// <summary>
	///     Chr(12)
	/// </summary>
	public const char FormFeed = '\f';

	/// <summary>
	///     Chr(0)
	/// </summary>
	public const char Null = '\0';

	/// <summary>
	///     Chr(7)
	/// </summary>
	public const char Alert = '\a';

	/// <summary>
	///     Chr(34)
	/// </summary>
	public const char Quote = '\"';

	/// <summary>
	///     Chr(9)
	/// </summary>
	public const char QuoteSingle = '\'';

	/// <summary>
	///     Chr(9)
	/// </summary>
	public const char Tab = '\t';

	/// <summary>
	///     Chr(11)
	/// </summary>
	public const char VerticalTab = '\v';
}
