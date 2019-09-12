namespace Ruya.Caching
{
    public interface IGlobalCachingProvider
    {
        void AddItem(string key, object value);
        object GetItem(string key);
    }
}