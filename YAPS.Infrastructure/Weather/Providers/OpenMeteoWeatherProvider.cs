using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Serilog;
using Yaps.Core.Abstractions;
using Yaps.Core.Models.Weather;
using Yaps.Infrastructure.Weather.Parsing;
using Yaps.Infrastructure.Weather.Parsing.OpenMeteo;

namespace Yaps.Infrastructure.Weather.Providers;

/// <summary>
/// Calls api.open-meteo.com/v1/forecast. No API key, ~10 000 free
/// requests/day, returns current + hourly arrays in one payload. The
/// hourly arrays cover today plus the next two days; we bucket by
/// hour-of-day into our four canonical periods and aggregate
/// (min/max for temperature, mode for the WMO code, mean for wind /
/// pressure / humidity).
/// </summary>
public sealed class OpenMeteoWeatherProvider : IWeatherProvider
{
    // Period → (startHour inclusive, endHour inclusive). Same convention
    // Yandex API uses (Night belongs to the start of the calendar day).
    private static readonly (int Start, int End)[] _periodWindows =
    {
        (0, 5),    // WeatherPeriod.…Night
        (6, 11),   // WeatherPeriod.…Morning
        (12, 17),  // WeatherPeriod.…Day
        (18, 23)   // WeatherPeriod.…Evening
    };

    private static readonly WeatherPeriod[][] _periodGrid =
    {
        new[] { WeatherPeriod.TodayNight,            WeatherPeriod.TodayMorning,            WeatherPeriod.TodayDay,            WeatherPeriod.TodayEvening },
        new[] { WeatherPeriod.TomorrowNight,         WeatherPeriod.TomorrowMorning,         WeatherPeriod.TomorrowDay,         WeatherPeriod.TomorrowEvening },
        new[] { WeatherPeriod.DayAfterTomorrowNight, WeatherPeriod.DayAfterTomorrowMorning, WeatherPeriod.DayAfterTomorrowDay, WeatherPeriod.DayAfterTomorrowEvening }
    };

    // hPa → mmHg. Open-Meteo can't be asked for pressure in mmHg directly.
    private const double HpaToMmHg = 0.7500616827;

    private readonly HttpClient _httpClient;
    private readonly IOptions<WeatherOptions> _options;

    // Same coalesce window as YandexApiWeatherProvider: the polling service
    // calls GetCurrentAsync and GetForecastAsync back-to-back on each tick,
    // and our /v1/forecast call returns both halves in one payload. The
    // memoisation lives for one tick; cross-tick freshness is controlled by
    // WeatherOptions.PollingInterval.
    private static readonly TimeSpan _coalesceWindow = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _fetchGate = new(1, 1);
    private OpenMeteoResponse? _cachedResponse;
    private DateTimeOffset _cachedAtUtc;

    public OpenMeteoWeatherProvider(HttpClient httpClient, IOptions<WeatherOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public string Name => "open-meteo";
    public WeatherCapabilities Capabilities => WeatherCapabilities.All;

    public async Task<WeatherSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var response = await FetchAsync(cancellationToken).ConfigureAwait(false);
        var cur = response?.Current;
        if (cur is null)
            return null;

        return new WeatherSnapshot
        {
            TemperatureCelsius = cur.Temperature2m,
            WeatherType = WeatherTypeMap.Lookup(WeatherTypeMap.OpenMeteoWmo, cur.WeatherCode),
            WindDirection = WindDirectionMap.FromDegrees(cur.WindDirection10m),
            WindSpeedMs = Round1(cur.WindSpeed10m),
            Pressure = ToMmHg(cur.SurfacePressureHpa),
            Humidity = cur.RelativeHumidity2m,
            ObservedAtUtc = DateTimeOffset.UtcNow,
            Source = Name
        };
    }

    public async Task<WeatherForecast?> GetForecastAsync(CancellationToken cancellationToken = default)
    {
        var response = await FetchAsync(cancellationToken).ConfigureAwait(false);
        var hourly = response?.Hourly;
        if (hourly?.Time is null || hourly.Time.Count == 0)
            return null;

        // The "today" reference is the local date of the first hourly entry —
        // Open-Meteo emits times in the requested timezone (we ask for "auto"),
        // so DateTime.Parse with AssumeLocal is fine here.
        if (!TryParseLocal(hourly.Time[0], out var anchor))
            return null;
        var today = anchor.Date;

        var periods = new Dictionary<WeatherPeriod, WeatherPeriodForecast>(_periodGrid.Length * _periodWindows.Length);

        for (int dayOffset = 0; dayOffset < _periodGrid.Length; dayOffset++)
        {
            var targetDate = today.AddDays(dayOffset);
            for (int periodIdx = 0; periodIdx < _periodWindows.Length; periodIdx++)
            {
                var (startHour, endHour) = _periodWindows[periodIdx];
                var period = _periodGrid[dayOffset][periodIdx];
                var cell = BuildPeriod(period, targetDate, startHour, endHour, hourly);
                if (cell is not null)
                    periods[period] = cell;
            }
        }

        return new WeatherForecast
        {
            Periods = periods,
            ObservedAtUtc = DateTimeOffset.UtcNow,
            Source = Name
        };
    }

    private static WeatherPeriodForecast? BuildPeriod(
        WeatherPeriod period,
        DateTime targetDate,
        int startHour,
        int endHour,
        OpenMeteoHourly hourly)
    {
        if (hourly.Time is null)
            return null;

        double? low = null, high = null;
        double windSum = 0, windDirSum = 0, pressureSum = 0, humiditySum = 0;
        int windCount = 0, windDirCount = 0, pressureCount = 0, humidityCount = 0;
        var codeHistogram = new Dictionary<int, int>(8);
        int matchedHours = 0;

        for (int i = 0; i < hourly.Time.Count; i++)
        {
            if (!TryParseLocal(hourly.Time[i], out var t))
                continue;
            if (t.Date != targetDate || t.Hour < startHour || t.Hour > endHour)
                continue;

            matchedHours++;

            var temp = ValueAt(hourly.Temperature2m, i);
            if (temp is double tv)
            {
                low = low is double lo ? Math.Min(lo, tv) : tv;
                high = high is double hi ? Math.Max(hi, tv) : tv;
            }

            if (ValueAt(hourly.WindSpeed10m, i) is double ws)
            {
                windSum += ws;
                windCount++;
            }

            if (ValueAt(hourly.WindDirection10m, i) is double wd)
            {
                windDirSum += wd;
                windDirCount++;
            }

            if (ValueAt(hourly.SurfacePressureHpa, i) is double p)
            {
                pressureSum += p;
                pressureCount++;
            }

            if (ValueAt(hourly.RelativeHumidity2m, i) is double h)
            {
                humiditySum += h;
                humidityCount++;
            }

            if (ValueAt(hourly.WeatherCode, i) is int wc)
                codeHistogram[wc] = codeHistogram.GetValueOrDefault(wc) + 1;
        }

        if (matchedHours == 0)
            return null;

        int? modeCode = null;
        int bestCount = 0;
        foreach (var kv in codeHistogram)
        {
            if (kv.Value > bestCount)
            {
                bestCount = kv.Value;
                modeCode = kv.Key;
            }
        }

        return new WeatherPeriodForecast
        {
            Period = period,
            Low = low,
            High = high,
            WindSpeedMs = windCount > 0 ? Round1(windSum / windCount) : null,
            WindDirection = windDirCount > 0 ? WindDirectionMap.FromDegrees(windDirSum / windDirCount) : WindDirection.Undefined,
            Pressure = pressureCount > 0 ? ToMmHg(pressureSum / pressureCount) : null,
            Humidity = humidityCount > 0 ? humiditySum / humidityCount : null,
            WeatherType = WeatherTypeMap.Lookup(WeatherTypeMap.OpenMeteoWmo, modeCode)
        };
    }

    private static T? ValueAt<T>(List<T?>? list, int i) where T : struct
        => list is not null && i < list.Count ? list[i] : null;

    // The Yandex API supplies pressure already as an integer mmHg; rounding
    // here keeps the displayed value in the same shape instead of bleeding the
    // hPa→mmHg conversion's 11 trailing decimals.
    private static double? ToMmHg(double? hpa) => hpa is double v ? Math.Round(v * HpaToMmHg) : null;

    // The Yandex API rounds wind speed to one
    // decimal; Open-Meteo returns the raw m/s with two, so the displayed
    // value here gets a trailing digit the rest of the UI never shows.
    private static double? Round1(double? ms) => ms is double v ? Math.Round(v, 1) : null;

    // Open-Meteo emits times like "2026-05-26T10:00" already shifted to the
    // requested timezone (we always pass timezone=auto). DateTime.Parse with
    // AssumeLocal preserves the local hour without a UTC round-trip — only
    // the Date and Hour fields are read downstream.
    private static bool TryParseLocal(string? raw, out DateTime value)
    {
        if (string.IsNullOrEmpty(raw))
        {
            value = default;
            return false;
        }
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out value);
    }

    private async Task<OpenMeteoResponse?> FetchAsync(CancellationToken cancellationToken)
    {
        if (_cachedResponse is not null && DateTimeOffset.UtcNow - _cachedAtUtc < _coalesceWindow)
            return _cachedResponse;

        await _fetchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedResponse is not null && DateTimeOffset.UtcNow - _cachedAtUtc < _coalesceWindow)
                return _cachedResponse;

            var opts = _options.Value;
            var lat = opts.Latitude.ToString(CultureInfo.InvariantCulture);
            var lon = opts.Longitude.ToString(CultureInfo.InvariantCulture);

            // forecast_days=3 covers today + 2 days = all 12 periods of the
            // WeatherForecast surface. timezone=auto so hourly timestamps
            // are already in local time of the requested coordinates —
            // no offset arithmetic on the consumer side.
            const string fields = "temperature_2m,weather_code,wind_speed_10m,wind_direction_10m,surface_pressure,relative_humidity_2m";
            var url = $"v1/forecast?latitude={lat}&longitude={lon}&current={fields}&hourly={fields}&forecast_days=3&timezone=auto&wind_speed_unit=ms";

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("Open-Meteo returned {Status} {Reason}", (int)response.StatusCode, response.ReasonPhrase);
                return null;
            }

            var parsed = await response.Content
                .ReadFromJsonAsync(OpenMeteoJsonContext.Default.OpenMeteoResponse, cancellationToken)
                .ConfigureAwait(false);

            if (parsed is not null)
            {
                _cachedResponse = parsed;
                _cachedAtUtc = DateTimeOffset.UtcNow;
            }
            return parsed;
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _fetchGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
