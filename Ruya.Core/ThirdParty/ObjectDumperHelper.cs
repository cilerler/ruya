using System.IO;

namespace Ruya.Core.ThirdParty
{
    public static class ObjectDumperHelper
    {
        public static string Dump(this object element, int depth)
        {
            string output;
            using (var stringWriter = new StringWriter())
            {
                ObjectDumper.Write(element, depth, stringWriter);
                output = stringWriter.ToString();
            }
            return output;
        }
    }
}