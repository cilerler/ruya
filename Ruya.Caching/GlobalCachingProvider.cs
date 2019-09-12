using System;

namespace Ruya.Caching
{
#warning Refactor
    public class GlobalCachingProvider : CachingProviderBase, IGlobalCachingProvider
    {

        #region Singelton
        /*
        protected GlobalCachingProvider()
        {
        }

        public static GlobalCachingProvider Instance => Nested.NestedInstance;

        private class Nested
        {
            internal static readonly GlobalCachingProvider NestedInstance = new GlobalCachingProvider();

            static Nested()
            {
            }
        }
        */
        private static readonly Lazy<GlobalCachingProvider> Lazy = new Lazy<GlobalCachingProvider>(() => new GlobalCachingProvider());

        public static GlobalCachingProvider Instance => Lazy.Value;

        protected GlobalCachingProvider()
        {
        }

        #endregion

        #region ICachingProvider

        public new virtual void AddItem(string key, object value)
        {
            base.AddItem(key, value);
        }

        public virtual object GetItem(string key)
        {
            return base.GetItem(key, true);
        }

        public new virtual object GetItem(string key, bool remove)
        {
            return base.GetItem(key, remove);
        }

        #endregion
    }
}