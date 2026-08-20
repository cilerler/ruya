using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Ruya.OpenTelemetry;

internal sealed class UrlExclusionMatcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private readonly Regex[] _patterns;

    public UrlExclusionMatcher(IEnumerable<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        _patterns = patterns
            .Select(pattern => new Regex(
                pattern,
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                RegexTimeout))
            .ToArray();
    }

    public bool IsExcluded(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var pattern in _patterns)
        {
            try
            {
                if (pattern.IsMatch(path))
                {
                    return true;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // An exclusion rule that cannot be evaluated safely must not allow telemetry through.
                return true;
            }
        }

        return false;
    }
}
