using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Ruya.Core.Properties;

namespace Ruya.Core
{
    public static class EnumHelper
    {
        private static readonly Random Randomizer = new Random();

        /// <summary>
        ///     Retrieves a random Enum item from the given Enum
        /// </summary>
        /// <typeparam name="T">Enum type</typeparam>
        /// <returns></returns>
        public static T GetRandomEnumItem<T>()
        {
            Array values = Enum.GetValues(typeof(T));
            return (T)values.GetValue(Randomizer.Next(values.Length));
        }

        /// <summary>
        ///     Retrieves the value of the given Enum based on its underlyning type
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown when argument is null</exception>
        public static object GetValue(this Enum input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }
            return Convert.ChangeType(input, input.GetTypeCode(), CultureInfo.InvariantCulture);
        }

        /// <summary>
        ///     Retrieves the flags based on given Enum
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        /// <remarks>it will not return default element (the one with value as 0) unless it is the only element</remarks>
        /// <exception cref="ArgumentNullException">Thrown when argument is null</exception>
        public static IEnumerable<Enum> ToEnumerable(this Enum input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            const byte defaultEnumValue = 0;
            bool defaultEnum = Convert.ToInt64(input, CultureInfo.InvariantCulture)
                                      .Equals(defaultEnumValue);
            return defaultEnum
                       ? new List<Enum>
                         {
                             input
                         }
                       : Enum.GetValues(input.GetType())
                             .Cast<Enum>()
                             .Where(item => input.HasFlag(item) && !Convert.ToInt64(item, CultureInfo.InvariantCulture)
                                                                           .Equals(defaultEnumValue));
        }

        /// <summary>
        ///     Retrieves the sum of the given Enum
        /// </summary>
        /// <param name="enumType"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown when argument is not Enum type</exception>
        /// <exception cref="ArgumentException">Thrown when argument is not Enum type</exception>
        public static long GetBitSum(this Type enumType)
        {
            if (enumType == null)
            {
                throw new ArgumentNullException(nameof(enumType));
            }
            if (!enumType.IsEnum)
            {
                throw new ArgumentException(Resources.ArgumentException_MustBeEnum);
            }
            return Enum.GetValues(enumType)
                       .Cast<object>()
                       .Sum(value => (long) Convert.ChangeType(value, typeof (long), CultureInfo.InvariantCulture));
        }

        public static long GetSum<T>(IEnumerable<T> input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            Type type = typeof(T);
            if (!type.IsEnum)
            {
                throw new ArgumentException(Resources.ArgumentException_MustBeEnum);
            }

            return input.Sum(enumItem => Convert.ToInt64(enumItem, CultureInfo.InvariantCulture));
        }

        /// <summary>
        ///     Retrieves the System.ComponentModel.Description attribute of the Enum
        /// </summary>
        /// <param name="input"></param>
        /// <returns>null if there is no description attribute</returns>
        /// <exception cref="ArgumentNullException">Thrown when argument is null</exception>
        public static string GetDescription(this Enum input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            Type type = input.GetType();
            FieldInfo fieldInfo = type.GetField(input.ToString());
            var descriptionAttribute = Attribute.GetCustomAttribute(fieldInfo, typeof (DescriptionAttribute)) as DescriptionAttribute;
            string output = descriptionAttribute?.Description;
            return output;
        }

        /// <summary>
        ///     Retreives the Enum from description
        /// </summary>
        /// <typeparam name="T">Enum type</typeparam>
        /// <param name="description"></param>
        /// <param name="caseSensitive"></param>
        /// <returns>depends of availability description, name itself or derfault value</returns>
        /// <exception cref="ArgumentException">Thrown when argument is not Enum type</exception>
        public static T GetEnumFromDescription<T>(string description, bool caseSensitive)
        {
            Type type = typeof (T);
            if (!type.IsEnum)
            {
                throw new ArgumentException(Resources.ArgumentException_MustBeEnum);
            }
            StringComparison stringComparison = caseSensitive
                                                    ? StringComparison.InvariantCulture
                                                    : StringComparison.InvariantCultureIgnoreCase;

            foreach (FieldInfo fieldInfo in from fieldInfo in type.GetFields()
                                            let descriptionAttribute = Attribute.GetCustomAttribute(fieldInfo, typeof (DescriptionAttribute)) as DescriptionAttribute
                                            let descriptionMatch = string.Equals(descriptionAttribute != null
                                                                                     ? descriptionAttribute.Description
                                                                                     : fieldInfo.Name, description, stringComparison)
                                            where descriptionMatch
                                            select fieldInfo)
            {
                return (T) fieldInfo.GetValue(null);
            }

            T noDescriptionFound = default(T);
            return noDescriptionFound;
        }
    }
}