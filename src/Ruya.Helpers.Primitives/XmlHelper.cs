using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace Ruya.Helpers.Primitives
{
    public class XmlHelper
    {
        private static XmlSerializer GetSerializer<T>(string rootElementName)
        {
            XmlSerializer serializer;
			if (string.IsNullOrWhiteSpace(rootElementName))
			{
				serializer = new XmlSerializer(typeof(T));
			}
			else
			{
				var xmlRootAttribute = new XmlRootAttribute
				                       {
					                       ElementName = rootElementName
					                     , IsNullable = true
				                       };
				serializer = new XmlSerializer(typeof(T)
				                             , xmlRootAttribute);
			}

			return serializer;
		}

		public T DeserializeFromXmlString<T>(string xml, string rootElementName = null)
		{
			T result;
			XmlSerializer serializer = GetSerializer<T>(rootElementName);
			using (TextReader reader = new StringReader(xml))
			{
				result = (T)serializer.Deserialize(reader);
			}
			return result;
		}

		public string SerializeToXmlString<T>(T source, string rootElementName = null, Encoding encoding = null)
		{
			if (encoding == null)
			{
				encoding = Encoding.UTF8;
			}

			var namespaces = new XmlSerializerNamespaces();
			namespaces.Add(string.Empty
			             , string.Empty);
			using (var stringWriter = new StringWriter())
			{
				var settings = new XmlWriterSettings
				               {
					               Encoding = encoding
					             , OmitXmlDeclaration = true
				               };
				XmlSerializer serializer = GetSerializer<T>(rootElementName);
				using (XmlWriter xmlWriter = XmlWriter.Create(stringWriter
				                                            , settings))
				{
					serializer.Serialize(xmlWriter
					                   , source
					                   , namespaces);
				}

				return stringWriter.ToString();
			}
		}
	}
}
