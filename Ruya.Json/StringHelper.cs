using System.Globalization;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Ruya.Json
{
    public static class StringHelper
    {
        public static XDocument ToXDocument(string jsonSource, string rootObjectName)
        {
            var jsonSerializerSettings = new JsonSerializerSettings
                                         {
                                             Converters =
                                             {
                                                 new XmlNodeConverter()
                                             },
                                             FloatParseHandling = FloatParseHandling.Decimal,
                                             NullValueHandling = NullValueHandling.Include
                                         };

            // HARD-CODED constant
            string modifiedData = string.Format(CultureInfo.InvariantCulture, Core.Constants.JsonTag, rootObjectName ,jsonSource);
            var xDocument = JsonConvert.DeserializeObject<XDocument>(modifiedData, jsonSerializerSettings);
            //x xDocument.Declaration = new XDeclaration("1.0", "utf-8", null);
            return xDocument;
        }
    }
}