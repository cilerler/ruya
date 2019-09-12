using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

namespace Ruya.Core
{
    public static class ObjectHelper
    {
        /// <summary>
        /// </summary>
        /// <param name="value"></param>
        /// <param name="useBinary"></param>
        /// <param name="isVerbose">if true, returns serialized object as bytes else returns itself</param>
        /// <param name="cultureInfo">if null uses CultureInfo.InvariantCulture</param>
        /// <param name="encoding">if null uses Encoding.Default</param>
        /// <returns></returns>
        public static string GetSize(this object value, bool useBinary, bool isVerbose, CultureInfo cultureInfo, Encoding encoding)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            string result;
            using (var stream = new MemoryStream())
            {
                if (useBinary)
                {
                    new BinaryFormatter().Serialize(stream, value);
                }
                else
                {
                    new NetDataContractSerializer().WriteObject(stream, value);
                }
                stream.Flush();
                long bytes = stream.Length;
                result = isVerbose
                             ? (encoding ?? Encoding.Default).GetString(stream.ToArray())
                             : bytes.ToString(cultureInfo ?? CultureInfo.InvariantCulture);
            }
            return result;
        }
    }
}