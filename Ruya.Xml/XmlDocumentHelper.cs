using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Xsl;
using Ruya.Diagnostics;
using Ruya.IO;

namespace Ruya.Xml
{
    public static class XmlDocumentHelper
    {
        [SuppressMessage("Microsoft.Design", "CA1059:MembersShouldNotExposeCertainConcreteTypes", MessageId = "System.Xml.XmlNode")]
        public static XmlSchemaSet ToXsd(this XmlDocument source)
        {
            var schema = new XmlSchemaInference();
            XmlSchemaSet xmlSchemaSet;
            using (var xmlNodeReader = new XmlNodeReader(source))
            {
                xmlSchemaSet = schema.InferSchema(xmlNodeReader);
            }
            return xmlSchemaSet;
        }

        public static string GetString(this XmlDocument xmlDocument)
        {
            if (xmlDocument == null)
            {
                throw new ArgumentNullException(nameof(xmlDocument));
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
                xmlDocument.Save(textWriter);
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

        public static XmlDocument CreateXmlDocument(string rootNodeName)
        {
            // HARD-CODED constant
            var rootTag = "root";
            if (!string.IsNullOrEmpty(rootNodeName))
            {
                rootTag = rootNodeName;
            }

            var xmlDocument = new XmlDocument();
            XmlDeclaration xmlDeclaration = xmlDocument.CreateXmlDeclaration("1.0", Encoding.UTF8.BodyName, null);
            XmlElement root = xmlDocument.DocumentElement;
            xmlDocument.InsertBefore(xmlDeclaration, root);
            XmlElement rootNodeElement = xmlDocument.CreateElement(rootTag);
            xmlDocument.AppendChild(rootNodeElement);
            return xmlDocument;
        }

        public static XmlDocument WrapXmlDocument(this XmlDocument xmlDocument, string rootNodeName)
        {            
            var output = CreateXmlDocument(rootNodeName);
            XmlElement root = xmlDocument.DocumentElement;
            if (root != null)
            {
                XmlNode importItem = output.ImportNode(root, true);
                output.DocumentElement?.AppendChild(importItem);
            }
            return output;
        }

        public static string Transform(this XmlDocument xmlDocument, string xslt)
        {
            var xslCompiledTransform = new XslCompiledTransform();

            using (TextReader textReader = new StringReader(xslt))
            {
                using (XmlReader xmlReader = XmlReader.Create(textReader))
                {
                    try
                    {
                        xslCompiledTransform.Load(xmlReader);
                    }
                    catch (XsltException xsltException)
                    {
                        Tracer.Instance.TraceEvent(TraceEventType.Error, 0, xsltException.Message);
                    }
                }
            }

            var results = new StringWriterExtended(Encoding.UTF8, CultureInfo.InvariantCulture);
            using (TextReader textReader = new StringReader(xmlDocument.OuterXml))
            {
                using (XmlReader reader = XmlReader.Create(textReader))
                {
                    try
                    {
                        xslCompiledTransform.Transform(reader, null, results);
                    }
                    catch (XsltException xsltException)
                    {
                        Tracer.Instance.TraceEvent(TraceEventType.Error, 0, xsltException.Message);
                    }
                }
            }
            return results.ToString();
        }
    }
}