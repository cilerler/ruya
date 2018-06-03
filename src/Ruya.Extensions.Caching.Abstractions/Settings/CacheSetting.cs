using System;

namespace Ruya.Extensions.Caching.Abstractions.Model
{
    public class CacheSetting : ICacheSetting
    {
        public bool Enabled { get; set; }
        public string Prefix { get; set; }
        public TimeSpan AbsoluteExpirationRelativeToNow { get; set; }
    }
}
