using System.Configuration;
using System.Xml;

namespace Ruya.Configuration
{
    public abstract class ConfigurationSettingElement : ConfigurationElement
    {
        /// <summary>
        ///     A unique name for this entry
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        ///     String representation for this entry, the Name
        /// </summary>
        public override string ToString()
        {
            return Name;
        }
    }
    
    public abstract class ConfigurationSettingElementWithInnerText : ConfigurationElement
    {
        public abstract string Name { get; set; }

        public override string ToString()
        {
            return Name;
        }

        public string Text { get; set; }

        protected override void DeserializeElement(XmlReader reader, bool serializeCollectionKey)
        {
            int attributeCount = reader.AttributeCount;
            for (var counter = 0; counter < attributeCount; counter++)
            {
                reader.MoveToAttribute(counter);
                string attributeName = reader.Name;
                this[attributeName] = reader.Value;
            }
            //then get the text content
            reader.MoveToElement();
            Text = reader.ReadElementContentAsString().Trim();
        }
    }
}