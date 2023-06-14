using System;
using System.Collections.Generic;

namespace Ruya.Primitives;

public struct HashCode
{
	private readonly int _value;

	public HashCode(int value)
	{
		_value = value;
	}

	public static HashCode Start { get; } = new(17);

	public static implicit operator int(HashCode hash)
	{
		return hash._value;
	}

	public readonly HashCode Hash<T>(T obj)
	{
		int h = EqualityComparer<T>.Default.GetHashCode(obj ?? throw new ArgumentNullException(nameof(obj)));
		return unchecked(new HashCode(_value * 31 + h));
	}

	public override int GetHashCode()
	{
		return _value;
	}
}
