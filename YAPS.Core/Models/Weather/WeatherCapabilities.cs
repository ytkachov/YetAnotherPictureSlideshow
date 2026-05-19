using System;

namespace Yaps.Core.Models.Weather;

/// <summary>
/// Declares which feeds a provider can produce so the polling service
/// can skip <see cref="IWeatherProvider.GetCurrentAsync"/> or
/// <see cref="IWeatherProvider.GetForecastAsync"/> when the provider
/// won't return useful data.
/// </summary>
[Flags]
public enum WeatherCapabilities
{
    None = 0,
    Current = 1 << 0,
    Forecast = 1 << 1,
    All = Current | Forecast
}
