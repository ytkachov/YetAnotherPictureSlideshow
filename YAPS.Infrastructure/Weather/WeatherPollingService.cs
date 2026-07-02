using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Yaps.Core.Abstractions;
using Yaps.Core.Models.Weather;

namespace Yaps.Infrastructure.Weather;

/// <summary>
/// Dual-tier polling with last-resort temperature fallback. Three
/// independent loops run side-by-side: <b>primary</b> and <b>secondary</b>
/// <see cref="IWeatherProvider"/> on their own cadences, plus an
/// <see cref="ICurrentTemperatureOverride"/> loop (NSU HTTP fetch) on
/// <c>max(primary, 5 min)</c>. After every tick the loop calls
/// <see cref="PublishPresentedAsync"/>, which picks the freshest tier by
/// priority — primary → secondary → NSU-only — and writes the
/// combined snapshot/forecast to <see cref="IWritableWeatherSnapshotStore"/>.
/// The store stays single-snapshot from the UI's perspective; the badge on
/// the live tile reads <see cref="WeatherSnapshot.Source"/> to surface
/// which tier is currently driving the screen.
/// </summary>
public sealed class WeatherPollingService : BackgroundService
{
    // NSU is now a lightweight HTTP fetch (was a headless-Chrome scrape), so
    // the floor is no longer about spin-up cost — it's courtesy to the
    // weather.nsu.ru endpoint plus the fact that a point-thermometer reading
    // doesn't move fast enough to warrant hitting it every primary tick.
    private static readonly TimeSpan MinNsuInterval = TimeSpan.FromMinutes(5);

    private readonly IWeatherProviderRegistry _registry;
    private readonly IEnumerable<ICurrentTemperatureOverride> _overrides;
    private readonly IWritableWeatherSnapshotStore _store;
    private readonly IOptions<WeatherOptions> _options;
    private readonly SemaphoreSlim _publishGate = new(1, 1);

    private TierState? _primary;
    private TierState? _secondary;
    private NsuReading? _nsu;

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
        // can finish coming up while the first network call goes out.
        await Task.Yield();

        var opts = _options.Value;
        var loops = new List<Task>(3);

        IWeatherProvider? primary = TryResolve(opts.SelectedProvider, "primary");
        if (primary is not null)
            loops.Add(RunLoopAsync("primary", opts.PollingInterval,
                ct => TierTickAsync(primary, isPrimary: true, ct), stoppingToken));

        if (!string.IsNullOrWhiteSpace(opts.SecondaryProvider))
        {
            if (string.Equals(opts.SecondaryProvider, opts.SelectedProvider, StringComparison.Ordinal))
            {
                Log.Information("Secondary weather provider '{Name}' matches primary; skipping secondary loop",
                    opts.SecondaryProvider);
            }
            else
            {
                IWeatherProvider? secondary = TryResolve(opts.SecondaryProvider!, "secondary");
                if (secondary is not null)
                    loops.Add(RunLoopAsync("secondary", opts.SecondaryPollingInterval,
                        ct => TierTickAsync(secondary, isPrimary: false, ct), stoppingToken));
            }
        }

        var nsuInterval = opts.PollingInterval > MinNsuInterval ? opts.PollingInterval : MinNsuInterval;
        loops.Add(RunLoopAsync("nsu", nsuInterval, NsuTickAsync, stoppingToken));

        try
        {
            await Task.WhenAll(loops).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown — one of the loops' Task.Delay caught the
            // cancellation and rethrew.
        }
    }

    private IWeatherProvider? TryResolve(string name, string label)
    {
        try
        {
            return _registry.Resolve(name);
        }
        catch (KeyNotFoundException ex)
        {
            Log.Error(ex, "Weather {Tier} provider '{Name}' is not registered; loop disabled", label, name);
            return null;
        }
    }

    private async Task RunLoopAsync(string label, TimeSpan interval, Func<CancellationToken, Task> tick, CancellationToken stoppingToken)
    {
        // Each loop yields onto its own worker so the three actually run
        // in parallel instead of queueing on this method's continuation
        // chain.
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await tick(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Weather {Loop} loop tick failed", label);
            }

            try
            {
                await PublishPresentedAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Weather PublishPresented after {Loop} tick failed", label);
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

    private async Task TierTickAsync(IWeatherProvider provider, bool isPrimary, CancellationToken ct)
    {
        WeatherSnapshot? current = null;
        WeatherForecast? forecast = null;

        if (provider.Capabilities.HasFlag(WeatherCapabilities.Current))
        {
            try
            {
                current = await provider.GetCurrentAsync(ct).ConfigureAwait(false);
            }
            // HttpClient.Timeout surfaces as TaskCanceledException without the
            // stopping token being cancelled — that's a transient network
            // failure, not a shutdown signal, so let it fall into the Warning
            // path instead of aborting the whole tick.
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                Log.Warning(ex, "{Provider} GetCurrentAsync failed", provider.Name);
            }
        }

        if (provider.Capabilities.HasFlag(WeatherCapabilities.Forecast))
        {
            try
            {
                forecast = await provider.GetForecastAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                Log.Warning(ex, "{Provider} GetForecastAsync failed", provider.Name);
            }
        }

        if (current is null && forecast is null)
            return;

        // Merge with the previous tier state so a tick that only refreshes
        // one half (e.g. forecast OK, current failed) doesn't drop the other.
        var previous = isPrimary ? Volatile.Read(ref _primary) : Volatile.Read(ref _secondary);
        var state = new TierState(
            current ?? previous?.Snapshot,
            forecast ?? previous?.Forecast,
            DateTimeOffset.UtcNow);

        if (isPrimary)
            Volatile.Write(ref _primary, state);
        else
            Volatile.Write(ref _secondary, state);

        Log.Debug("Weather {Tier} tick: provider={Provider} current={HasCurrent} forecast={HasForecast}",
            isPrimary ? "primary" : "secondary", provider.Name, current is not null, forecast is not null);
    }

    private async Task NsuTickAsync(CancellationToken ct)
    {
        foreach (var ov in _overrides)
        {
            double? value;
            try
            {
                value = await ov.GetCurrentTemperatureCelsiusAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                Log.Warning(ex, "Temperature override '{Source}' failed; keeping previous value", ov.SourceName);
                continue;
            }

            if (value is null)
                continue;

            Volatile.Write(ref _nsu, new NsuReading(value.Value, ov.SourceName, DateTimeOffset.UtcNow));
            Log.Debug("Temperature override '{Source}' updated: {Value}", ov.SourceName, value);
        }
    }

    private async Task PublishPresentedAsync(CancellationToken ct)
    {
        await _publishGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var opts = _options.Value;
            var now = DateTimeOffset.UtcNow;
            var primary = Volatile.Read(ref _primary);
            var secondary = Volatile.Read(ref _secondary);
            var nsu = Volatile.Read(ref _nsu);

            // 2× interval freshness window: one missed tick is still
            // tolerated as "fresh enough"; two consecutive misses bumps
            // the tier out of consideration for the next.
            var primaryFresh = primary is not null
                               && now - primary.SuccessAtUtc <= opts.PollingInterval + opts.PollingInterval;
            var secondaryFresh = secondary is not null
                                 && now - secondary.SuccessAtUtc <= opts.SecondaryPollingInterval + opts.SecondaryPollingInterval;
            var nsuEffectiveInterval = opts.PollingInterval > MinNsuInterval ? opts.PollingInterval : MinNsuInterval;
            var nsuFresh = nsu is not null
                           && now - nsu.SuccessAtUtc <= nsuEffectiveInterval + nsuEffectiveInterval;

            WeatherSnapshot? current;
            WeatherForecast? forecast;

            if (primaryFresh)
            {
                current = primary!.Snapshot;
                forecast = primary.Forecast;
            }
            else if (secondaryFresh)
            {
                current = secondary!.Snapshot;
                forecast = secondary.Forecast;
            }
            else if (nsuFresh)
            {
                // Last-resort: temperature only, every other field stays
                // null so the UI hides those tiles via the existing
                // WeatherStatusToVisibility converter. Source names the
                // override so the badge flips to "НГУ".
                current = new WeatherSnapshot
                {
                    TemperatureCelsius = nsu!.Temperature,
                    Source = nsu.SourceName,
                    ObservedAtUtc = nsu.SuccessAtUtc
                };
                forecast = null;
            }
            else
            {
                current = null;
                forecast = null;
            }

            // Apply the NSU temperature override on top of provider-sourced
            // snapshots (Stage 5 semantics). When the snapshot itself IS the
            // NSU fallback the override would be a no-op; skip it then so
            // the badge keeps reading "НГУ" rather than reverting to whichever
            // provider's Source the snapshot carried earlier.
            if (current is not null
                && nsuFresh
                && opts.ApplyCurrentTemperatureOverride
                && !string.Equals(current.Source, nsu!.SourceName, StringComparison.Ordinal))
            {
                current = current with
                {
                    TemperatureCelsius = nsu.Temperature,
                    TemperatureOverrideApplied = nsu.SourceName
                };
            }

            _store.Set(current, forecast);
        }
        finally
        {
            _publishGate.Release();
        }
    }

    private sealed record TierState(WeatherSnapshot? Snapshot, WeatherForecast? Forecast, DateTimeOffset SuccessAtUtc);
    private sealed record NsuReading(double Temperature, string SourceName, DateTimeOffset SuccessAtUtc);
}
