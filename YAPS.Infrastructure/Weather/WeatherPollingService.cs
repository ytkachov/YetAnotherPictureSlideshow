using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Yaps.Core.Abstractions;
using Yaps.Core.Models.Weather;

namespace Yaps.Infrastructure.Weather;

/// <summary>
/// Owns the polling cadence for the screensaver. Resolves the configured
/// provider once per tick (so a hot-swap landing in Stage 6 only needs to
/// flip <c>WeatherOptions.SelectedProvider</c> through IOptionsMonitor),
/// applies every registered <see cref="ICurrentTemperatureOverride"/>
/// to the result, and writes the snapshot into
/// <see cref="IWritableWeatherSnapshotStore"/>. Errors are logged but
/// never thrown out of <see cref="ExecuteAsync"/> — an unhandled
/// exception escaping <see cref="BackgroundService"/> tears down the
/// whole host.
/// </summary>
public sealed class WeatherPollingService : BackgroundService
{
    private readonly IWeatherProviderRegistry _registry;
    private readonly IEnumerable<ICurrentTemperatureOverride> _overrides;
    private readonly IWritableWeatherSnapshotStore _store;
    private readonly IOptions<WeatherOptions> _options;

    public WeatherPollingService(
        IWeatherProviderRegistry registry,
        IEnumerable<ICurrentTemperatureOverride> overrides,
        IWritableWeatherSnapshotStore store,
        IOptions<WeatherOptions> options)
    {
        _registry = registry;
        _overrides = overrides;
        _store = store;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield so Host.StartAsync returns promptly; the rest of the host
        // can finish coming up while we make the first network call.
        await Task.Yield();

        var interval = _options.Value.PollingInterval;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown; don't log as an error.
                break;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "WeatherPollingService tick failed");
            }

            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        var opts = _options.Value;
        IWeatherProvider provider;
        try
        {
            provider = _registry.Resolve(opts.SelectedProvider);
        }
        catch (KeyNotFoundException ex)
        {
            Log.Error(ex, "Configured weather provider '{Name}' is not registered; nothing to do", opts.SelectedProvider);
            return;
        }

        WeatherSnapshot? current = null;
        WeatherForecast? forecast = null;

        if (provider.Capabilities.HasFlag(WeatherCapabilities.Current))
        {
            try
            {
                current = await provider.GetCurrentAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Warning(ex, "{Provider} GetCurrentAsync failed", opts.SelectedProvider);
            }
        }

        if (current is not null && opts.ApplyCurrentTemperatureOverride)
            current = await ApplyOverridesAsync(current, ct).ConfigureAwait(false);

        if (provider.Capabilities.HasFlag(WeatherCapabilities.Forecast))
        {
            try
            {
                forecast = await provider.GetForecastAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Warning(ex, "{Provider} GetForecastAsync failed", opts.SelectedProvider);
            }
        }

        // Only commit if at least one feed produced data — otherwise keep
        // the previous (possibly older) snapshot instead of nulling the UI.
        if (current is not null || forecast is not null)
        {
            var nextCurrent = current ?? _store.Current;
            var nextForecast = forecast ?? _store.Forecast;
            _store.Set(nextCurrent, nextForecast);
            Log.Debug("Weather poll: provider={Provider} current={HasCurrent} forecast={HasForecast}",
                opts.SelectedProvider, current is not null, forecast is not null);
        }
    }

    private async Task<WeatherSnapshot> ApplyOverridesAsync(WeatherSnapshot baseline, CancellationToken ct)
    {
        var snapshot = baseline;
        foreach (var ov in _overrides)
        {
            double? value;
            try
            {
                value = await ov.GetCurrentTemperatureCelsiusAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Warning(ex, "Temperature override '{Source}' failed; keeping previous value", ov.SourceName);
                continue;
            }

            if (value is null)
                continue;

            Log.Debug("Override '{Source}': {Old} -> {New}", ov.SourceName, snapshot.TemperatureCelsius, value);
            snapshot = snapshot with
            {
                TemperatureCelsius = value,
                TemperatureOverrideApplied = ov.SourceName
            };
        }
        return snapshot;
    }
}
