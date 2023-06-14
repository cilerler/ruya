using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Ruya.Helpers.Primitives;

public class TimeZoneHelper
{
	public enum TimeZoneId
	{
		EasternStandardTime,
		CentralStandardTime,
		PacificStandardTime
	}

	public static Dictionary<TimeZoneId, TimeZoneType> TimeZoneIds => new()
	{
		{ TimeZoneId.EasternStandardTime, new TimeZoneType { Windows = "Eastern Standard Time", NonWindows = "America/New_York" } },
		{ TimeZoneId.CentralStandardTime, new TimeZoneType { Windows = "Central Standard Time", NonWindows = "America/Chicago" } },
		{ TimeZoneId.PacificStandardTime, new TimeZoneType { Windows = "Pacific Standard Time", NonWindows = "America/Los_Angeles" } }
	};

	// TODO Use UTC instead of EST
	public static DateTime GetStandardTime(TimeZoneId timeZone, DateTime? dateTime = null)
	{
		TimeZoneInfo timeZoneInfo = GetStandardTimeZoneInfo(timeZone);
		DateTime output = TimeZoneInfo.ConvertTime(dateTime ?? DateTime.Now, TimeZoneInfo.Local, timeZoneInfo);
		return output;
	}

	public static DateTime GetStandardTimeAsLocal(TimeZoneId timeZone, DateTime? dateTime = null)
	{
		TimeZoneInfo timeZoneInfo = GetStandardTimeZoneInfo(timeZone);
		DateTime output = TimeZoneInfo.ConvertTime(dateTime ?? DateTime.Now, timeZoneInfo, TimeZoneInfo.Local);
		return output;
	}

	public static TimeZoneInfo GetStandardTimeZoneInfo(TimeZoneId timeZone)
	{
		if (!TimeZoneIds.TryGetValue(timeZone, out TimeZoneType timeZoneId)) throw new ArgumentOutOfRangeException(nameof(timeZoneId));
		string id = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			? timeZoneId.Windows
			: timeZoneId.NonWindows;
		var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(id);
		return timeZoneInfo;
	}

	public class TimeZoneType
	{
		public string Windows { get; set; }
		public string NonWindows { get; set; }
	}
}
