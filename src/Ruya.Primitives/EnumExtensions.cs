using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Ruya.Primitives;

public static class EnumExtensions
{
    private static readonly ConcurrentDictionary<Enum, Lazy<string>> DisplayNameCache = new();
    private static readonly ConcurrentDictionary<(Type, string), Lazy<Enum>> ReverseCache = new();

    /// <summary>
    /// Retrieves the first custom attribute of the specified type for a given enum value.
    /// Returns null if no attribute is found.
    /// </summary>
    public static T? GetAttributeOfType<T>(this Enum enumValue) where T : Attribute
    {
        ArgumentNullException.ThrowIfNull(enumValue);

        return enumValue
            .GetType()
            .GetMember(enumValue.ToString())
            .FirstOrDefault()?
            .GetCustomAttribute<T>(inherit: false);
    }

    /// <summary>
    /// Returns the [Display(Name="...")] attribute value if present; otherwise, the enum's name.
    /// If used on a flags enum with multiple bits set, the raw 'ToString()' will be returned
    /// (e.g., "ValueA, ValueB").
    /// </summary>
    public static string GetDisplayName(this Enum enumValue)
    {
        ArgumentNullException.ThrowIfNull(enumValue);

        return DisplayNameCache.GetOrAdd(enumValue,
            static e => new Lazy<string>(
                () => e.GetAttributeOfType<DisplayAttribute>()?.Name ?? e.ToString(),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    /// <summary>
    /// Converts the specified displayName to the matching enum value of type T.
    /// - Ignores case
    /// - Throws if displayName is null, empty, or not found
    /// - Throws if multiple values share the same display name
    /// </summary>
    public static T ToEnum<T>(this string displayName) where T : Enum
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var key = (typeof(T), displayName.ToUpperInvariant());

        return (T)ReverseCache.GetOrAdd(key,
            static k => new Lazy<Enum>(() =>
            {
                var matches = Enum.GetValues(k.Item1)
                    .Cast<Enum>()
                    .Where(v => string.Equals(v.GetDisplayName(), k.Item2, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                return matches.Count switch
                {
                    0 => throw new ArgumentException($"No enum value of type {k.Item1} found with display name '{k.Item2}'."),
                    1 => matches[0],
                    _ => throw new ArgumentException($"Multiple enum values of type {k.Item1} share the display name '{k.Item2}'.")
                };
            }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    /// <summary>
    /// Returns all display names for the enum type T, including [Display(Name="...")] overrides.
    /// </summary>
    public static List<string> GetAllDisplayNames<T>() where T : Enum
    {
        return Enum.GetValues(typeof(T))
            .Cast<Enum>()
            .Select(e => e.GetDisplayName())
            .ToList();
    }

    /// <summary>
    /// For a [Flags] enum, joins each set bit's display name with a comma.
    /// For a non-flags enum, simply returns GetDisplayName().
    /// </summary>
    public static string GetFlagsDisplayName(this Enum enumValue)
    {
        ArgumentNullException.ThrowIfNull(enumValue);

        var enumType = enumValue.GetType();
        if (!enumType.IsDefined(typeof(FlagsAttribute), false))
            return enumValue.GetDisplayName();

        var activeBits = Enum.GetValues(enumType)
            .Cast<Enum>()
            .Where(bit => Convert.ToInt64(bit) != 0 && enumValue.HasFlag(bit))
            .Select(b => b.GetDisplayName());

        return string.Join(", ", activeBits);
    }

    /// <summary>
    /// Converts a comma-separated string of display names into a single [Flags] enum value.
    /// Will throw if the enum is not marked with [Flags] or if any display name is invalid.
    /// </summary>
    public static T ToFlagsEnum<T>(this string displayName) where T : Enum
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var enumType = typeof(T);
        if (!enumType.IsDefined(typeof(FlagsAttribute), false))
            return displayName.ToEnum<T>();

        var parts = displayName.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var accumulatedValue = parts
            .Select(part => Convert.ToInt64(part.ToEnum<T>()))
            .Aggregate((long)0, (acc, val) => acc | val);

        return (T)Enum.ToObject(enumType, accumulatedValue);
    }
}
