using System;
using System.Threading;
using Yaps.Core.Abstractions;
using Yaps.Core.Models.Weather;

namespace Yaps.Infrastructure.Weather;

/// <summary>
/// Internal write-side companion to <see cref="IWeatherSnapshotStore"/>.
/// Only the polling service should hold this; UI consumers see the
/// read-only interface so they can't accidentally clobber the snapshot.
/// </summary>
public interface IWritableWeatherSnapshotStore : IWeatherSnapshotStore
{
    void Set(WeatherSnapshot? current, WeatherForecast? forecast);
}

/// <summary>
/// Simple in-memory store. Fields are read by the UI dispatcher and
/// written by the polling background thread, hence
/// <see cref="Volatile.Read{T}"/>/<see cref="Volatile.Write{T}"/> — same
/// pattern <c>LocalImageInfo._placeName</c> uses for the geocoding race.
/// </summary>
public sealed class WeatherSnapshotStore : IWritableWeatherSnapshotStore
{
    private WeatherSnapshot? _current;
    private WeatherForecast? _forecast;
    private DateTimeOffset? _lastUpdatedUtc;

    public WeatherSnapshot? Current => Volatile.Read(ref _current);
    public WeatherForecast? Forecast => Volatile.Read(ref _forecast);

    public DateTimeOffset? LastUpdatedUtc
    {
        get
        {
            // DateTimeOffset isn't a reference type so Volatile.Read can't
            // operate on it directly; round-trip via the underlying ticks.
            var local = _lastUpdatedUtc;
            Thread.MemoryBarrier();
            return local;
        }
    }

    public event Action? Updated;

    public void Set(WeatherSnapshot? current, WeatherForecast? forecast)
    {
        Volatile.Write(ref _current, current);
        Volatile.Write(ref _forecast, forecast);
        _lastUpdatedUtc = DateTimeOffset.UtcNow;
        Thread.MemoryBarrier();

        // Fire on a snapshot of the multicast delegate so a subscriber
        // unsubscribing mid-invoke doesn't trip a NullReferenceException.
        Updated?.Invoke();
    }
}
