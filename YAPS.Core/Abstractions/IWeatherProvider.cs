using System;
using System.Threading;
using System.Threading.Tasks;
using Yaps.Core.Models.Weather;

namespace Yaps.Core.Abstractions;

/// <summary>
/// Source of current weather and/or a 3-day forecast. Implementations
/// return null on handled failure (network down, parser miss, missing
/// API key) rather than throwing — the polling service treats null as
/// "keep the previous snapshot". The synchronous <c>release()</c> of the
/// legacy interface is gone; lifetime is owned by the DI container and
/// any per-call resources (WebDriver, HttpResponseMessage) are disposed
/// inside the implementation.
/// </summary>
public interface IWeatherProvider : IAsyncDisposable
{
    string Name { get; }
    WeatherCapabilities Capabilities { get; }

    Task<WeatherSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task<WeatherForecast?> GetForecastAsync(CancellationToken cancellationToken = default);
}
