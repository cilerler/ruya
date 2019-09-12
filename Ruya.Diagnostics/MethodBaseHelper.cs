using System.Reflection;

namespace Ruya.Diagnostics
{
    public static class MethodBaseHelper
    {
        public static string GetQualifiedName(this MethodBase methodBase)
        {
            string fullQualifiedClassName = methodBase.GetFullQualifiedClassName();
            string methodName = methodBase.Name;
            return $"{fullQualifiedClassName}.{methodName}";
        }

        public static string GetFullQualifiedClassName(this MethodBase methodBase)
        {
            string fullQualifiedClassName = methodBase.DeclaringType?.Name;
            return fullQualifiedClassName;
        }
    }
}
