using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

namespace Ruya.Core
{
    public static class GenericsHelper
    {
        // TEST method DeepCopy
        public static T DeepCopy<T>(this T source)
        {
            if (!typeof (T).IsSerializable)
            {
                // HARD-CODED constant
                throw new ArgumentException("The type must be serializable.", nameof(source));
            }
            
            if (ReferenceEquals(source, null))
            {
                return default(T);
            }

            object result;
            using (var memoryStream = new MemoryStream())
            {
                var binaryFormatter = new BinaryFormatter();
                binaryFormatter.Serialize(memoryStream, source);
                memoryStream.Seek(0, SeekOrigin.Begin);
                result = binaryFormatter.Deserialize(memoryStream);
                //x CA2202 memoryStream.Close();
            }
            return (T) result;
        }

        // TEST method Clone
        public static T Clone<T>(this T source)
        {
            var dataContractSerializer = new DataContractSerializer(typeof (T));
            using (var memoryStream = new MemoryStream())
            {
                dataContractSerializer.WriteObject(memoryStream, source);
                memoryStream.Seek(0, SeekOrigin.Begin);
                return (T) dataContractSerializer.ReadObject(memoryStream);
            }
        }
    }
}