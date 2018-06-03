using System;

namespace Ruya.Extensions.Caching.Abstractions.Model
{
    public interface ICacheSetting
    {
        bool Enabled { get; set; }
        string Prefix { get; set; }
        TimeSpan AbsoluteExpirationRelativeToNow { get; set; }
    }
}
