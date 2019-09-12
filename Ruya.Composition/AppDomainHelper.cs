using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Permissions;
using Ruya.Diagnostics;
using Ruya.IO;

namespace Ruya.Composition
{
    public sealed class AppDomainHelper : MarshalByRefObject, IDisposable
    {
        [SecurityPermission(SecurityAction.Demand, Flags = SecurityPermissionFlag.Infrastructure)]
        public override object InitializeLifetimeService()
        {
            return null;
        }

        private AppDomainSetup _appDomainSetup;
        public List<AppDomain> AppDomains { get; } = new List<AppDomain>();

        #region IDisposable Members

        public void Dispose()
        {
            for (int counter = AppDomains.Count - 1; counter < 0; counter--)
            {
                UnloadAppDomain(AppDomains[counter]);
            }
        }

        #endregion

        private void PrepareDirectories(bool clearShadowCopyDirectories, bool clearCachePath)
        {
            string baseDirectory = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;
            
            // HARD-CODED constant
            const string shadowCopyDirectoriesDirectoryName = "Plugins";
            string shadowCopyBaseDirectory = Path.Combine(baseDirectory, shadowCopyDirectoriesDirectoryName);
            if (clearShadowCopyDirectories) DirectoryHelper.DeleteDirectory(shadowCopyBaseDirectory);
            DirectoryHelper.CreateDirectory(shadowCopyBaseDirectory);

            ShadowCopyDirectories = string.Join(";", DirectoryHelper.EnumerateDirectories(shadowCopyBaseDirectory));

            // HARD-CODED constant
            const string cacheDirectoryName = "ShadowCopyCache";
            string cachePath = Path.Combine(baseDirectory, cacheDirectoryName);
            if (clearCachePath) DirectoryHelper.DeleteDirectory(cachePath);
            DirectoryHelper.CreateDirectory(cachePath);

        }

        public string CachePath { get; set; }
        public string ShadowCopyDirectories{ get; set; }

        public void Setup(bool clearShadowCopyDirectories, bool clearCachePath)
        {
            PrepareDirectories(clearShadowCopyDirectories, clearCachePath);
            if (_appDomainSetup != null)
            {
                return;
            }
            _appDomainSetup = new AppDomainSetup
                              {
                                  CachePath = CachePath,
                                  ShadowCopyDirectories = ShadowCopyDirectories,
                                  ShadowCopyFiles = true.ToString(),
                                  LoaderOptimization = LoaderOptimization.MultiDomain
                              };
                        
            // HARD-CODED constant
            Tracer.Instance.TraceEvent(System.Diagnostics.TraceEventType.Verbose, 0, "AppDomainSetup created.");
        }

        public T CreateNewAppDomain<T>(string domainName) where T : class
        {
            // ReSharper disable once InvertIf
            if (AppDomains.FirstOrDefault(ad => ad.FriendlyName.Equals(domainName)) == null)
            {
                AppDomain domain = AppDomain.CreateDomain(domainName, AppDomain.CurrentDomain.Evidence, _appDomainSetup);
                AppDomains.Add(domain);

                // HARD-CODED constant
                Tracer.Instance.TraceEvent(System.Diagnostics.TraceEventType.Verbose, 0, "AppDomain {0} created.", domainName);

                //! This bypasses the Main method as it's not executing it.
                object instance = domain.CreateInstanceAndUnwrap(typeof (T).Assembly.FullName, typeof (T).FullName);
                Instances.Add(new KeyValuePair<string, object>(domainName, instance));

                // HARD-CODED constant
                Tracer.Instance.TraceEvent(System.Diagnostics.TraceEventType.Verbose, 0, "AppDomain Instance {0} created.", typeof(T).FullName);
                //x return AppDomainInstances.Last() as T;
                return instance as T;
            }
            return null;
        }

        public List<KeyValuePair<string, object>> Instances { get; } = new List<KeyValuePair<string, object>>();

        private void UnloadAppDomain(AppDomain domain)
        {
            string domainFriendlyName = domain.FriendlyName;
            Instances.RemoveAll(instance => instance.Key.Equals(domainFriendlyName));
            if (AppDomains.Contains(domain))
            {
                AppDomains.Remove(domain);
            }
            if (!domain.IsFinalizingForUnload())
            {
                AppDomain.Unload(domain);
                // HARD-CODED constant
                Tracer.Instance.TraceEvent(System.Diagnostics.TraceEventType.Verbose, 0, "AppDomain {0} unloaded.", domainFriendlyName);
            }
        }

        public bool UnloadAppDomain(string friendlyName)
        {
            AppDomain value = AppDomains.FirstOrDefault(ad => ad.FriendlyName.Equals(friendlyName));
            if (value == null)
            {
                return false;
            }
            UnloadAppDomain(value);
            return true;
        }

        #region Singleton

        private static readonly Lazy<AppDomainHelper> Lazy = new Lazy<AppDomainHelper>(() => new AppDomainHelper());

        public static AppDomainHelper Instance => Lazy.Value;

        private AppDomainHelper()
        {
        }

        #endregion
    }
}