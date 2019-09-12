using System;
using System.IO;

namespace Ruya.IO
{
    public static class MemoryStreamHelper
    {
        public static void WriteToFile(this MemoryStream memoryStream, string path)
        {
            if (ReferenceEquals(memoryStream, null)) throw new ArgumentNullException(nameof(memoryStream));
            using (FileStream fileStream = File.OpenWrite(path))
            {
                memoryStream.WriteTo(fileStream);
            }
        }
    }
}