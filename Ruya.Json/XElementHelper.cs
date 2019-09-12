using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Ruya.Json
{
    public static class XElementHelper
    {
        internal static XElement Sort(this XElement element)
        {
            return new XElement(element.Name, element.Attributes(), from child in element.Nodes()
                                                                      where child.NodeType != XmlNodeType.Element
                                                                      select child, from child in element.Elements()
                                                                                    orderby child.Name.ToString()
                                                                                    select Sort(child));
        }
    }
}