using System;

namespace Yaps.Core.Models.Weather;

/// <summary>
/// Single point-in-time measurement. Nullable doubles are deliberate:
/// providers can return partial data (e.g. temperature without pressure
/// when scraping fails on one field), and the UI hides the corresponding
/// widget when the value is null. <see cref="TemperatureOverrideApplied"/>
/// names the override that replaced the temperature, if any — useful for
/// log triage when the displayed value disagrees with the upstream feed.
/// </summary>
public sealed record WeatherSnapshot
{
    public double? TemperatureCelsius { get; init; }
    public double? Pressure { get; init; }
    public double? Humidity { get; init; }
    public double? WindSpeedMs { get; init; }
    public WindDirection WindDirection { get; init; } = WindDirection.Undefined;
    public WeatherType WeatherType { get; init; } = WeatherType.Undefined;

    public DateTimeOffset ObservedAtUtc { get; init; }
    public string Source { get; init; } = string.Empty;
    public string? TemperatureOverrideApplied { get; init; }
}
