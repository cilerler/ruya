using System;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace Ruya.Json
{
    public static class JObjectHelper
    {
        public static XDocument ToXDocument(this JObject json, string rootObjectName)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }
            string jsonSource = json.ToString();
            return StringHelper.ToXDocument(jsonSource, rootObjectName);
        }
    }
}