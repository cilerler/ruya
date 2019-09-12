using System.Collections.Generic;
using System.Xml.Linq;

namespace Ruya.Xml
{
    public static class KeyValuePairHelper
    {
        public static XElement ToXElement(this KeyValuePair<object, object> input)
        {
            var xElement = new XElement(input.Key.ToString());
            var keys = input.Value as Dictionary<object, object>;
            if (keys != null)
            {
                foreach (KeyValuePair<object, object> key in keys)
                {
                    xElement.Add(key.ToXElement());
                }
            }
            else
            {
                xElement.Value = input.Value?.ToString() ?? string.Empty;
            }
            return xElement;
        }
    }
}