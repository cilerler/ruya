using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Ruya.Xml
{
    public static class DictionaryHelper
    {
        // TEST method ToXDocument
        // COMMENT method ToXDocument
        public static XDocument ToXDocument(this Dictionary<object, object> dictionary, string rootElementName)
        {
            if (dictionary==null) throw new ArgumentNullException(nameof(dictionary));
            var document = new XDocument();
            var dataRoot = new XElement(rootElementName);
            foreach (KeyValuePair<object, object> key in dictionary)
            {
                dataRoot.Add(key.ToXElement());
            }
            document.Add(dataRoot);
            return document;
        }
    }
}