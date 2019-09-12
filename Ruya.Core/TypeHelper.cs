using System;
using System.Collections;
using System.Globalization;

namespace Ruya.Core
{
    public static class TypeHelper
    {

        // TEST property DotNetTypes 
        // COMMENT property DotNetTypes 
        //? consider to remove this
        public static Hashtable DotNetTypes => new Hashtable
                                                  {
                                                      [typeof (bool)] = true,
                                                      [typeof (char)] = ControlChars.Space,
                                                      [typeof (string)] = string.Empty,
                                                      [typeof (byte)] = byte.MaxValue,
                                                      [typeof (sbyte)] = sbyte.MaxValue,
                                                      [typeof (short)] = short.MaxValue,
                                                      [typeof (int)] = int.MaxValue,
                                                      [typeof (long)] = long.MaxValue,
                                                      [typeof (ushort)] = ushort.MaxValue,
                                                      [typeof (uint)] = uint.MaxValue,
                                                      [typeof (ulong)] = ulong.MaxValue,
                                                      [typeof (float)] = float.MaxValue,
                                                      [typeof (double)] = double.MaxValue,
                                                      [typeof (decimal)] = decimal.MaxValue,
                                                      [typeof (DateTime)] = DateTime.MaxValue,
                                                      [typeof (TimeSpan)] = TimeSpan.MaxValue,
                                                      [typeof (Guid)] = Guid.NewGuid(),
                                                      [typeof (byte[])] = new byte[]
                                                                          {
                                                                          },
                                                      [typeof (object)] = new object()
                                                  };

        // COMMENT property SqlTypes
        public static Hashtable SqlTypes => new Hashtable
                                               {
                                                   [typeof (bool)] = "bit",
                                                   [typeof (char)] = "nchar",
                                                   [typeof (string)] = "varchar (MAX)",
                                                   [typeof (byte)] = "tinyint",
                                                   [typeof (short)] = "smallint",
                                                   [typeof (int)] = "int",
                                                   [typeof (long)] = "bigint",
                                                   [typeof (ushort)] = "smallint",
                                                   [typeof (uint)] = "int",
                                                   [typeof (ulong)] = "bigint",
                                                   [typeof (float)] = "real",
                                                   [typeof (double)] = "float",
                                                   [typeof (decimal)] = "numeric",
                                                   [typeof (DateTime)] = "datetime2",
                                                   [typeof (TimeSpan)] = "time",
                                                   [typeof (Guid)] = "uniqueidentifier",
                                                   [typeof (byte[])] = "varbinary",
                                                   [typeof (object)] = "sql_variant"
                                               };

        /// <summary>
        ///     Evaluates the type and returns if it is numeric
        /// </summary>
        /// <param name="type"></param>
        /// <param name="includeUnderlyingType">if true, <see cref="F:System.Boolean" /> value will be considered as numeric</param>
        /// <returns></returns>
        public static bool IsNumeric(this Type type, bool includeUnderlyingType)
        {
            bool output;
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean:
                    output = includeUnderlyingType;
                    break;
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    output = true;
                    break;
                default:
                    output = false;
                    break;
            }
            return output;
        }

        /// <summary>
        ///     Retrieves the type from given name.  Adds System prefix upon request.
        /// </summary>
        /// <param name="typeName"></param>
        /// <param name="addSystemPrefix">Adds System. in front of the name</param>
        /// <returns>null if there is no Type available</returns>
        public static Type GetTypeFromName(string typeName, bool addSystemPrefix)
        {
            string name = typeName;
            if (addSystemPrefix)
            {
                const string prefix = "System";
                name = string.Format(CultureInfo.InvariantCulture, "{0}.{1}", prefix, typeName);
            }
            Type output;
            try
            {
                output = Type.GetType(name);
            }
            catch (ArgumentNullException)
            {
                output = null;
            }
            return output;
        }
    }
}