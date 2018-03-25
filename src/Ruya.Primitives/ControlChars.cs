namespace Ruya.Primitives
{
    public static class ControlChars
    {
        /// <summary>
        ///     Chr(8)
        /// </summary>
        public const char BackSpace = '\b';

        /// <summary>
        ///     Chr(13)
        /// </summary>
        public const char CarriageReturn = '\r';

        /// <summary>
        ///     Chr(10)
        /// </summary>
        public const char LineFeed = '\n';

        /// <summary>
        ///     Chr(13) & Chr(10)
        /// </summary>
        public const string CarriageReturnLineFeed = "\r\n";

        /// <summary>
        ///     Chr(13) & Chr(10) (same as CarriageReturnLineFeed)
        /// </summary>
        public const string NewLine = CarriageReturnLineFeed;

        public const char FormFeed = '\f';

        /// <summary>
        ///     Chr(0)
        /// </summary>
        public const char Null = '\0';

        public const char Alert = '\a';
        public const char Quote = '\"';
        public const char QuoteSingle = '\'';

        /// <summary>
        ///     Chr(9)
        /// </summary>
        public const char Tab = '\t';

        /// <summary>
        ///     Chr(11)
        /// </summary>
        public const char VerticalTab = '\v';

        public const char BackSlash = '\\';
        public const char Space = ' ';

        public const char Comma = ',';
    }
}
