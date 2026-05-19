using System;
using Yaps.Core.Models.Weather;

namespace Yaps.Core.Abstractions;

/// <summary>
/// Read-only view of the last successful poll. Refreshed by
/// <c>WeatherPollingService</c>; consumed by <c>WeatherInformer</c>.
/// <see cref="Updated"/> fires after both <see cref="Current"/> and
/// <see cref="Forecast"/> for a single tick have been written so
/// subscribers see a consistent pair.
/// </summary>
public interface IWeatherSnapshotStore
{
    WeatherSnapshot? Current { get; }
    WeatherForecast? Forecast { get; }
    DateTimeOffset? LastUpdatedUtc { get; }

    event Action? Updated;
}
