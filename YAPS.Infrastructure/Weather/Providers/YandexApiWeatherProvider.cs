using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Serilog;
using Yaps.Core.Abstractions;
using Yaps.Core.Models.Weather;
using Yaps.Infrastructure.Weather.Parsing;
using Yaps.Infrastructure.Weather.Parsing.YandexApi;

namespace Yaps.Infrastructure.Weather.Providers;

/// <summary>
/// Calls api.weather.yandex.ru/v2/forecast and folds the response into
/// our canonical <see cref="WeatherSnapshot"/>/<see cref="WeatherForecast"/>.
/// HttpClient is supplied by HttpClientFactory so cooler concerns
/// (timeout, header, DNS rotation) live in the registration extension.
/// </summary>
public sealed class YandexApiWeatherProvider : IWeatherProvider
{
    private static readonly WeatherPeriod[] _periodOrder =
    {
        WeatherPeriod.TodayMorning,            WeatherPeriod.TodayDay,            WeatherPeriod.TodayEvening,            WeatherPeriod.TodayNight,
        WeatherPeriod.TomorrowMorning,         WeatherPeriod.TomorrowDay,         WeatherPeriod.TomorrowEvening,         WeatherPeriod.TomorrowNight,
        WeatherPeriod.DayAfterTomorrowMorning, WeatherPeriod.DayAfterTomorrowDay, WeatherPeriod.DayAfterTomorrowEvening, WeatherPeriod.DayAfterTomorrowNight
    };

    private readonly HttpClient _httpClient;
    private readonly IOptions<WeatherOptions> _options;

    public YandexApiWeatherProvider(HttpClient httpClient, IOptions<WeatherOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public string Name => "yandex-api";
    public WeatherCapabilities Capabilities => WeatherCapabilities.All;

    public async Task<WeatherSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var response = await FetchAsync(cancellationToken).ConfigureAwait(false);
        var fact = response?.Fact;
        if (fact is null)
            return null;

        return new WeatherSnapshot
        {
            TemperatureCelsius = fact.Temp,
            WeatherType = WeatherTypeMap.Lookup(WeatherTypeMap.YandexApi, fact.Icon),
            WindDirection = WindDirectionMap.Lookup(WindDirectionMap.YandexApi, fact.WindDir),
            WindSpeedMs = fact.WindSpeed,
            Pressure = fact.PressureMm ?? response!.Info?.DefPressureMm,
            Humidity = fact.Humidity,
            ObservedAtUtc = DateTimeOffset.UtcNow,
            Source = Name
        };
    }

    public async Task<WeatherForecast?> GetForecastAsync(CancellationToken cancellationToken = default)
    {
        var response = await FetchAsync(cancellationToken).ConfigureAwait(false);
        if (response?.Forecasts is null || response.Forecasts.Count == 0)
            return null;

        var periods = new Dictionary<WeatherPeriod, WeatherPeriodForecast>(_periodOrder.Length);
        var ordinal = 0;
        foreach (var day in response.Forecasts)
        {
            var parts = day.Parts;
            if (parts is null) { ordinal += 4; continue; }

            TryAdd(periods, _periodOrder, ref ordinal, parts.Morning);
            TryAdd(periods, _periodOrder, ref ordinal, parts.Day);
            TryAdd(periods, _periodOrder, ref ordinal, parts.Evening);
            TryAdd(periods, _periodOrder, ref ordinal, parts.Night);

            if (ordinal >= _periodOrder.Length)
                break;
        }

        return new WeatherForecast
        {
            Periods = periods,
            ObservedAtUtc = DateTimeOffset.UtcNow,
            Source = Name
        };
    }

    private static void TryAdd(
        Dictionary<WeatherPeriod, WeatherPeriodForecast> bag,
        WeatherPeriod[] order,
        ref int ordinal,
        YandexApiDayPart? part)
    {
        if (ordinal >= order.Length) { ordinal++; return; }
        var period = order[ordinal++];
        if (part is null) return;

        var low = part.TempMin ?? part.TempAvg;
        var high = part.TempMax ?? part.TempAvg;
        bag[period] = new WeatherPeriodForecast
        {
            Period = period,
            Low = low,
            High = high,
            WeatherType = WeatherTypeMap.Lookup(WeatherTypeMap.YandexApi, part.Icon),
            WindDirection = WindDirectionMap.Lookup(WindDirectionMap.YandexApi, part.WindDir),
            WindSpeedMs = part.WindSpeed,
            Pressure = part.PressureMm,
            Humidity = part.Humidity
        };
    }

    private async Task<YandexApiResponse?> FetchAsync(CancellationToken cancellationToken)
    {
        var opts = _options.Value;
        var apiKey = opts.YandexApiKey;
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "get_from_bitwarden")
        {
            Log.Warning("Yandex weather API key is not configured (Settings.YandexApiKey={Key}); skipping fetch", apiKey ?? "<null>");
            return null;
        }

        var url = $"v2/forecast?lat={opts.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}&lon={opts.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("X-Yandex-Weather-Key", apiKey);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("Yandex weather API returned {Status} {Reason}", (int)response.StatusCode, response.ReasonPhrase);
            return null;
        }

        return await response.Content
            .ReadFromJsonAsync(YandexApiJsonContext.Default.YandexApiResponse, cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
