using System;
using System.Diagnostics;
using System.Reflection;

namespace Ruya.Diagnostics
{
    public static class AssemblyHelper
    {
        /// <summary>
        /// Returns a <see cref="T:System.Diagnostics.FileVersionInfo"/> representing the version information associated with the specified assembly.
        /// </summary>
        /// <param name="assembly"></param>
        /// A <see cref="T:System.Diagnostics.FileVersionInfo"/> containing information about the assembly. If the assembly did not contain version information, the <see cref="T:System.Diagnostics.FileVersionInfo"/> contains only the name of the assembly requested.
        public static FileVersionInfo GetFileVersionInfo(this Assembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }
            FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
            return fileVersionInfo;
        }

        /// <summary>
        /// Retrieves title attribute applied to assembly.
        /// </summary>
        /// <param name="assembly"></param>
        /// <returns>null, if there is no title attribute</returns>
        public static string GetTitleAttribute(this Assembly assembly)
        {
            var attribute = Attribute.GetCustomAttribute(assembly, typeof(AssemblyTitleAttribute), false) as AssemblyTitleAttribute;
            string configuration = attribute?.Title;           
            return configuration;
        }

        /// <summary>
        /// Retrieves configuration attribute applied to assembly.
        /// </summary>
        /// <param name="assembly"></param>
        /// <returns>null, if there is no configuration attribute</returns>
        public static string GetConfigurationAttribute(this Assembly assembly)
        {
            var attribute = Attribute.GetCustomAttribute(assembly, typeof(AssemblyConfigurationAttribute), false) as AssemblyConfigurationAttribute;
            string configuration = attribute?.Configuration;
            return configuration;
        }
    }
}