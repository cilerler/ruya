using System.Collections.Generic;
using System.IO;
using System.Runtime.Caching;

namespace Ruya.Caching
{
#warning Refactor
    public static class Trial
    {
        /*
                GlobalCachingProvider.Instance.AddItem("Message", text);
                var message = GlobalCachingProvider.Instance.GetItem("Message") as string;
        */

        public static string GetFile(string path)
        {
            const string keyword = "filecontents";
            ObjectCache cache = MemoryCache.Default;
            var fileContents = cache[keyword] as string;

            if (fileContents != null)
            {
                return fileContents;
            }
            var filePaths = new List<string>
                            {
                                path
                            };
            var policy = new CacheItemPolicy();
            policy.ChangeMonitors.Add(new HostFileChangeMonitor(filePaths));
            fileContents = File.ReadAllText(path);
            cache.Set(keyword, fileContents, policy);

            return fileContents;
        }
    }
}