using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;

namespace Ruya.Configuration
{
    /// <summary>
    ///     A collection of list types all with a Name attribute
    /// </summary>
    /// <typeparam name="T">The type of collection, inherited from SettingsCollectionElement</typeparam>
    public class ConfigurationSettingElementCollection<T> : ConfigurationElementCollection where T : ConfigurationElement, new()
    {
        /// <summary>
        ///     Accessor by key
        /// </summary>
        public new T this[string key] => BaseGet(key) as T;

        /// <summary>
        ///     Accessor by index
        /// </summary>
        public T this[int index] => BaseGet(index) as T;

        /// <summary>
        ///     Returns all the elements in this collection, type-cased out
        /// </summary>
        public IEnumerable<T> All => this.Cast<T>();

        /// <summary>
        ///     Default constructor for this element
        /// </summary>
        protected override ConfigurationElement CreateNewElement()
        {
            return new T();
        }

        /// <summary>
        ///     Default by-key fetch for this element
        /// </summary>
        protected override object GetElementKey(ConfigurationElement element)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }
            return element.ToString();
        }

        public void Add(T element)
        {
            BaseAdd(element);
        }
    }
}