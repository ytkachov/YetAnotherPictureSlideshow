namespace Yaps.Core.Models.Weather;

/// <summary>
/// Slots the slideshow's weather strip uses. <see cref="Now"/> is the
/// live measurement; the other 12 cover the visible 3-day forecast.
/// Order is load-bearing for the Configuration UI's day/period layout,
/// don't reshuffle.
/// </summary>
public enum WeatherPeriod
{
    Undefined,
    Now,
    TodayMorning,
    TodayDay,
    TodayEvening,
    TodayNight,
    TomorrowMorning,
    TomorrowDay,
    TomorrowEvening,
    TomorrowNight,
    DayAfterTomorrowMorning,
    DayAfterTomorrowDay,
    DayAfterTomorrowEvening,
    DayAfterTomorrowNight
}
