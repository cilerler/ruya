using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Ruya.Core
{
    public static class DateTimeHelper
    {
        private static readonly Random Randomizer = new Random();

        /// <summary>
        ///     Retrieves a random DateTime
        /// </summary>
        /// <returns></returns>
        public static DateTime GetRandomDateTime(DateTime from, DateTime to)
        {
            TimeSpan range = to - from;
            var randomTimeSpan = new TimeSpan((long) (Randomizer.NextDouble()*range.Ticks));
            return from + randomTimeSpan;
        }

        /// <summary>
        ///     Takes the date data from dateValue and time data from timeValue and returns a new value as combined
        /// </summary>
        /// <param name="dateValue"></param>
        /// <param name="timeValue"></param>
        /// <returns></returns>
        public static DateTime CombineDateTime(DateTime dateValue, DateTime timeValue)
        {
            //x return new DateTime(dateValue.Year, dateValue.Month, dateValue.Day, timeValue.Hour, timeValue.Minute, timeValue.Second);
            return dateValue.Date + timeValue.TimeOfDay;
        }

        // TEST method GetDateTimeUtcNowWithUtcOffset
        /// <summary>
        ///     Retrieves the UTC DateTime.Now and adds UTC Offset as suffix
        /// </summary>
        /// <returns></returns>
        [SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public static string GetDateTimeUtcNowWithUtcOffset()
        {
            string datetime = DateTime.UtcNow.ToString(Constants.FileSystemSafeDateTime, CultureInfo.InvariantCulture);
            int utcOffset = TimeZone.CurrentTimeZone.GetUtcOffset(DateTime.Now)
                                    .Hours;
            string output = datetime + utcOffset;
            return output;
        }

        // COMMENT method GetWeekOfYear
        // TEST method GetWeekOfYear
        public static int GetWeekOfYear(this DateTime time, CalendarWeekRule rule, DayOfWeek firstDayOfWeek)
        {
            int output = new GregorianCalendar().GetWeekOfYear(time, rule, firstDayOfWeek);
            return output;
        }

        private static DateTime _msSinceEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        // COMMENT method ToJavaScript
        // TEST method ToJavaScript
        public static long ToJavaScript(this DateTime input)
        {   
            var timeSpan = new TimeSpan(_msSinceEpoch.Ticks);
            DateTime time = input.Subtract(timeSpan);
            long output = time.Ticks/10000;
            return output;
        }

        public static DateTime GetDateTimeFromSeconds(double input)
        {
            DateTime outputUtc = _msSinceEpoch.Add(TimeSpan.FromSeconds(input));
            DateTime output = outputUtc.ToLocalTime();
            return output;
        }
    }
}