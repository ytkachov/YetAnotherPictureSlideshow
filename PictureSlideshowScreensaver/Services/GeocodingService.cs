using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Serilog;

public class GeocodingResult
{
    public string PlaceName { get; set; }
    public string FullResponse { get; set; }
}

public static class GeocodingService
{
    private static readonly TimeSpan _minInterval = TimeSpan.FromSeconds(10);

    private static readonly HttpClient _httpClient = CreateClient();
    private static readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private static DateTime _lastRequestUtc = DateTime.MinValue;

    private static readonly string[] _poiFields = {
        "tourism", "amenity", "leisure", "historic",
        "building", "aeroway", "railway", "shop", "office", "name"
    };

    private static readonly string[] _cityFields = {
        "city", "town", "village", "hamlet", "municipality", "county"
    };

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        // Nominatim requires a valid User-Agent. Set it once instead of
        // re-parsing on every request.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PictureSlideshowScreensaver/1.0");
        return client;
    }

    public static Task<GeocodingResult> ReverseGeocodeAsync(double latitude, double longitude)
        => ReverseGeocodeAsync(latitude, longitude, CancellationToken.None);

    public static async Task<GeocodingResult> ReverseGeocodeAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        // Serialise all callers through a SemaphoreSlim so concurrent slideshow
        // tasks don't race past Nominatim's rate limit; an async Task.Delay
        // replaces the previous blocking Thread.Sleep inside lock(), which
        // could starve the thread pool.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sinceLast = DateTime.UtcNow - _lastRequestUtc;
            if (sinceLast < _minInterval)
                await Task.Delay(_minInterval - sinceLast, cancellationToken).ConfigureAwait(false);

            try
            {
                string url = $"https://nominatim.openstreetmap.org/reverse?lat={latitude}&lon={longitude}&format=json&zoom=18&accept-language=ru";
                string response = await _httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

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
                // Stamp the time even on failure so a flaky endpoint doesn't
                // let us hammer it.
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
        var json = JObject.Parse(response);
        if (!json.ContainsKey("address"))
        {
            return new GeocodingResult
            {
                PlaceName = null,
                FullResponse = response
            };
        }

        var address = json["address"];

        string landmark = FirstNonEmpty(address, _poiFields);
        string city = FirstNonEmpty(address, _cityFields);

        string shortName;
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

    private static string FirstNonEmpty(JToken address, string[] fields)
    {
        foreach (var field in fields)
        {
            var val = address[field];
            if (val != null && !string.IsNullOrEmpty(val.ToString()))
                return val.ToString();
        }
        return null;
    }
}
