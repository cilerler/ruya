using System;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Ruya.Json
{
    public static class XDocumentHelper
    {
        public static XDocument Sort(this XDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            return new XDocument(document.Declaration, from child in document.Nodes()
                                                       where child.NodeType != XmlNodeType.Element
                                                       select child, document.Root.Sort());
        }
        
    }
}