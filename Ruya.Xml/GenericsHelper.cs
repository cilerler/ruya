using System;
using System.IO;
using System.Xml.Serialization;
using Ruya.Diagnostics;

namespace Ruya.Xml
{
    public static class GenericsHelper
    {
        // TEST method ToObject
        // COMMENT method ToObject
        public static T ToObject<T>(this string xmlInput)
        {
            T output = default(T);

            using (TextReader reader = new StringReader(xmlInput))
            {
                try
                {
                    output = (T) new XmlSerializer(typeof (T)).Deserialize(reader);
                }
                catch (InvalidOperationException)
                {
                    // HARD-CODED constant
                    Tracer.Instance.TraceEvent(System.Diagnostics.TraceEventType.Warning, 0, @"input is not valid XML, return default");
                }
            }

            return output;
        }
    }
}