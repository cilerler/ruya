using System;
using System.Xml;
using System.Xml.XPath;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Formatting = Newtonsoft.Json.Formatting;

namespace Ruya.Json
{
    public static class XmlDocumentHelper
    {
        [Obsolete("Use ToJObject instead", true)]
        public static JObject FromXml(this XmlDocument input)
        {
            string jsonString = JsonConvert.SerializeXmlNode(input, Formatting.Indented, true);
            JObject output = JObject.Parse(jsonString);
            return output;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1059:MembersShouldNotExposeCertainConcreteTypes", MessageId = "System.Xml.XmlNode")]
        public static JObject ToJObject(this XmlDocument input) 
        {
            string jsonString = JsonConvert.SerializeXmlNode(input, Formatting.Indented, true);
            JObject output = JObject.Parse(jsonString);
            return output;
        }
    }
}