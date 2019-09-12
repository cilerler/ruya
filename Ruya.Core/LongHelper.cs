using System;

namespace Ruya.Core
{
    public static class LongHelper
    {
        // TEST method FromJavaScript
        // COMMENT method FromJavaScript
        public static DateTime FromJavaScript(this long value)
        {
            var msSinceEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0);
            return msSinceEpoch.AddMilliseconds(value);
        }
    }
}