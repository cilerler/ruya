using System;
using System.Xml;
using System.Xml.Linq;

namespace Ruya.Xml
{
    public static class XNodeHelper
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1059:MembersShouldNotExposeCertainConcreteTypes", MessageId = "System.Xml.XmlNode")]
        public static XmlDocument ToXmlDocument(this XNode source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var xmlDocument = new XmlDocument();
            using (XmlReader xmlReader = source.CreateReader())
            {
                xmlDocument.Load(xmlReader);
            }
            //x xmlDocument.CreateXmlDeclaration("1.0", "utf-8", null);
            return xmlDocument;
        }
    }
}