using System;
using System.Diagnostics;
using System.Runtime.Caching;
using Ruya.Diagnostics;

namespace Ruya.Caching
{
#warning Refactor

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    public abstract class CachingProviderBase
    {
        // HARD-CODED constant

        protected MemoryCache Cache { get; set; } = new MemoryCache("CachingProvider");

        private static readonly object Padlock = new object();

        protected virtual void AddItem(string key, object value)
        {
            lock (Padlock)
            {
                Cache.Add(key, value, DateTimeOffset.MaxValue);
            }
        }

        protected virtual void RemoveItem(string key)
        {
            lock (Padlock)
            {
                Cache.Remove(key);
            }
        }

        protected virtual object GetItem(string key, bool remove)
        {
            lock (Padlock)
            {
                object result = Cache[key];

                if (result != null)
                {
                    if (remove)
                    {
                        Cache.Remove(key);
                    }
                }
                else
                {
                    // HARD-CODED constant
                    Tracer.Instance.TraceEvent(TraceEventType.Warning, 0, "CachingProvider-GetItem: KeyNotFound: " + key);
                }

                return result;
            }
        }
    }
}