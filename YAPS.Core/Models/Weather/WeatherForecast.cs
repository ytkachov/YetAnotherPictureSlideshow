using System;
using System.Collections.Generic;

namespace Yaps.Core.Models.Weather;

/// <summary>
/// A day-part forecast cell. <see cref="Low"/> and <see cref="High"/> may
/// be equal when the provider only reports one temperature for the period
/// (Yandex API does this for past day-parts).
/// </summary>
public sealed record WeatherPeriodForecast
{
    public WeatherPeriod Period { get; init; }
    public double? Low { get; init; }
    public double? High { get; init; }
    public double? Pressure { get; init; }
    public double? Humidity { get; init; }
    public double? WindSpeedMs { get; init; }
    public WindDirection WindDirection { get; init; } = WindDirection.Undefined;
    public WeatherType WeatherType { get; init; } = WeatherType.Undefined;
}

/// <summary>
/// Bundle of day-part forecasts keyed by <see cref="WeatherPeriod"/> so
/// the WeatherInformer can look up the period currently bound to a
/// <c>Weather</c> UserControl in O(1).
/// </summary>
public sealed record WeatherForecast
{
    public IReadOnlyDictionary<WeatherPeriod, WeatherPeriodForecast> Periods { get; init; } =
        new Dictionary<WeatherPeriod, WeatherPeriodForecast>();

    public DateTimeOffset ObservedAtUtc { get; init; }
    public string Source { get; init; } = string.Empty;
}
