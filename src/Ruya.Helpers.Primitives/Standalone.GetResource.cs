using System.IO;
using System.Reflection;
using System.Text;

namespace Ruya.Helpers.Primitives
{
    public partial class Standalone
    {
        public static string GetResource(Assembly assembly, string fileName, string prefix)
        {
            const char separator = '.';
            string output;
            string entryAssemblyName = assembly.GetName()
                                               .Name;
            var resourceName = new StringBuilder();
            resourceName.Append(entryAssemblyName);
            if (!string.IsNullOrWhiteSpace(prefix))
            {
                resourceName.Append(separator);
                resourceName.Append(prefix);
            }
            resourceName.Append(separator);
            resourceName.Append(fileName);
            Stream resourceStream = assembly.GetManifestResourceStream(resourceName.ToString());
            using (var reader = new StreamReader(resourceStream, Encoding.UTF8))
            {
                output = reader.ReadToEnd();
            }
            return output;
        }
    }
}
