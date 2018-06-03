using System;

namespace Ruya.Extensions.Caching.Abstractions.Settings
{
    public class CacheSetting : ICacheSetting
    {
        public bool Enabled { get; set; }
        public string Prefix { get; set; }
        public TimeSpan AbsoluteExpirationRelativeToNow { get; set; }
    }
}
