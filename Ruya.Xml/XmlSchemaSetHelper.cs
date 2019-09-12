using System;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using Ruya.IO;

namespace Ruya.Xml
{
    public static class XmlSchemaSetHelper
    {
        public static string GetString(this XmlSchemaSet xmlSchemaSet)
        {
            if (xmlSchemaSet == null)
            {
                throw new ArgumentNullException(nameof(xmlSchemaSet));
            }

            var settings = new XmlWriterSettings
                           {
                               Encoding = new UTF8Encoding(false, false),
                               Indent = true,
                               OmitXmlDeclaration = false
                           };

            string output;
            StringWriterExtended textWriter = null;
            XmlWriter xmlWriter = null;
            try
            {
                textWriter = new StringWriterExtended(Encoding.UTF8, CultureInfo.InvariantCulture);
                xmlWriter = XmlWriter.Create(textWriter, settings);
                
                    foreach (XmlSchema s in xmlSchemaSet.Schemas())
                    {
                        s.Write(xmlWriter);
                    }
                
                output = textWriter.ToString();
            }
            finally
            {
                if (xmlWriter != null)
                {
#if NET45_OR_GREATER
                    xmlWriter.Dispose();
#endif
                }
                else
                {
                    textWriter?.Dispose();
                }
            }

            return output;
        }
    }
}