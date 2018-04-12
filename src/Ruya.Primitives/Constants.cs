using System;

namespace Ruya.Primitives
{
    public static class Constants
    {
        public const string Production = "PRODUCTION";
        public const string Staging = "STAGING";
        public const string Development = "DEVELOPMENT";
        public const string Release = "RELEASE";
        public const string Debug = "DEBUG";
        public const string Trace = "TRACE";
        public const string FileSystemSafeDate = "yyyyMMdd";
        public const string FileSystemSafeTime = "HHmmss";
        public const string FileSystemSafeDateTime = "yyyyMMddHHmmss";
        public const string FileSystemSafeDateTimeOffset = "yyyyMMddHHmmsszz";
        public const string XmlTag = "<{0}>{1}</{0}>";
        public const string JsonTag = "{{\"" + "{0}" + "\":" + "{1}" + "}}";
        public const string HtmlNewLine = "<br />";
        public const string WildCardStar = "*";
        public const string WildCardQuestionMark = "?";
        public static readonly double KiloByte = Math.Pow(2, 10);
        public static readonly double MegaByte = Math.Pow(2, 20);
        public static readonly double GigaByte = Math.Pow(2, 30);
        public static readonly double TeraByte = Math.Pow(2, 40);
    }
}
