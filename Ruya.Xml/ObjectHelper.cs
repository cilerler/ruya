using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using Ruya.Diagnostics;

namespace Ruya.Xml
{
    public static class ObjectHelper
    {
        // TEST method ToObject
        // COMMENT method ToObject
        public static string ToXml<T>(this T value)
        {
            string output = string.Empty;
            if (value == null)
            {
                return output;
            }

            try
            {
                var xmlserializer = new XmlSerializer(typeof(T));
                using (var stringWriter = new StringWriter())
                {
                    using (XmlWriter writer = XmlWriter.Create(stringWriter, new XmlWriterSettings { Indent = true }))
                    {
                        xmlserializer.Serialize(writer, value);
                        output = stringWriter.ToString();
                    }
                }
            }
            catch (InvalidOperationException ioe)
            {
                // HARD-CODED constant
                Tracer.Instance.TraceData(System.Diagnostics.TraceEventType.Warning, 0, ioe);
            }

            return output;
        }
    }
}
