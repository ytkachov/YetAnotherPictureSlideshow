using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Yaps.Core.Abstractions;
using Yaps.Core.Models;

namespace Yaps.Infrastructure.Geocoding;

/// <summary>
/// IGeocoder implementation backed by Nominatim (OpenStreetMap's free
/// reverse-geocoding endpoint). Serialises all requests through a
/// SemaphoreSlim so concurrent callers can't race past Nominatim's
/// rate limit, and uses an injected HttpClient (so HttpClientFactory
/// can manage timeout / DNS / connection lifetime centrally).
/// </summary>
public sealed class NominatimGeocoder : IGeocoder
{
    // Stricter than Nominatim's 1 req/sec official limit on purpose:
    // the screensaver only resolves a place name once per photo and we
    // don't want to look like a scraper. Keep aligned with the
    // previous static GeocodingService.
    private static readonly TimeSpan _minInterval = TimeSpan.FromSeconds(10);

    private static readonly string[] _poiFields =
    {
        "tourism", "amenity", "leisure", "historic",
        "building", "aeroway", "railway", "shop", "office", "name"
    };

    private static readonly string[] _cityFields =
    {
        "city", "town", "village", "hamlet", "municipality", "county"
    };

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _lastRequestUtc = DateTime.MinValue;

    public NominatimGeocoder(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<GeocodingResult?> ReverseGeocodeAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sinceLast = DateTime.UtcNow - _lastRequestUtc;
            if (sinceLast < _minInterval)
                await Task.Delay(_minInterval - sinceLast, cancellationToken).ConfigureAwait(false);

            try
            {
                var url = $"https://nominatim.openstreetmap.org/reverse?lat={latitude}&lon={longitude}&format=json&zoom=18&accept-language=ru";
                var response = await _httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
                return ParseResponse(response);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Geocoding failed for lat={Lat}, lon={Lon}", latitude, longitude);
                return null;
            }
            finally
            {
                // Stamp even on failure so a flaky endpoint doesn't let us
                // hammer it.
                _lastRequestUtc = DateTime.UtcNow;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static GeocodingResult ParseResponse(string response)
    {
        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;

        if (!root.TryGetProperty("address", out var address) || address.ValueKind != JsonValueKind.Object)
        {
            return new GeocodingResult { FullResponse = response };
        }

        var landmark = FirstNonEmpty(address, _poiFields);
        var city = FirstNonEmpty(address, _cityFields);

        string? shortName;
        if (!string.IsNullOrEmpty(landmark) && !string.IsNullOrEmpty(city))
            shortName = $"{landmark}, {city}";
        else if (!string.IsNullOrEmpty(city))
            shortName = city;
        else
            shortName = null;

        return new GeocodingResult
        {
            PlaceName = shortName,
            FullResponse = response
        };
    }

    private static string? FirstNonEmpty(JsonElement address, string[] fields)
    {
        foreach (var field in fields)
        {
            if (address.TryGetProperty(field, out var val) && val.ValueKind == JsonValueKind.String)
            {
                var s = val.GetString();
                if (!string.IsNullOrEmpty(s))
                    return s;
            }
        }
        return null;
    }
}
