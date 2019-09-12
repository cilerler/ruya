using System;
using System.Globalization;

namespace Ruya.Core
{
    public static class TimeSpanHelper
    {
        /// <summary>
        /// Converts formatted string value of time to TimeSpan based on InvariantCulture with format 'g' 
        /// </summary>
        /// <param name="value">formatted string e.g. 17:14:48</param>
        /// <returns></returns>
        public static TimeSpan GetInterval(string value)
        {   
            const string format = "g";
            CultureInfo culture = CultureInfo.InvariantCulture;
            TimeSpan interval;
            return TimeSpan.TryParseExact(value, format, culture, out interval)
                       ? interval
                       : new TimeSpan();
        }
    }
}
