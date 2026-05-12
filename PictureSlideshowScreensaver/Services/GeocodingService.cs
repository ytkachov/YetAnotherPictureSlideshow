using System;
using System.Collections.Generic;
using System.Net.Http;
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
    private static readonly HttpClient _httpClient = new HttpClient();
    private static DateTime _lastRequestTime = DateTime.MinValue;
    private static readonly object _lock = new object();

    private static readonly string[] _poiFields = {
        "tourism", "amenity", "leisure", "historic",
        "building", "aeroway", "railway", "shop", "office", "name"
    };

    private static readonly string[] _cityFields = {
        "city", "town", "village", "hamlet", "municipality", "county"
    };

    public static async Task<GeocodingResult> ReverseGeocodeAsync(double latitude, double longitude)
    {
        lock (_lock)
        {
            var elapsed = DateTime.Now - _lastRequestTime;
            if (elapsed.TotalMilliseconds < 1100)
            {
                int delay = 1100 - (int)elapsed.TotalMilliseconds;
                System.Threading.Thread.Sleep(delay);
            }
        }

        try
        {
            string url = $"https://nominatim.openstreetmap.org/reverse?lat={latitude}&lon={longitude}&format=json&zoom=18&accept-language=ru";
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PictureSlideshowScreensaver/1.0");

            string response = await _httpClient.GetStringAsync(url);

            lock (_lock)
                _lastRequestTime = DateTime.Now;

            var json = JObject.Parse(response);

            string landmark = null;
            if (json.ContainsKey("address"))
            {
                var address = json["address"];

                foreach (var field in _poiFields)
                {
                    var val = address[field];
                    if (val != null && !string.IsNullOrEmpty(val.ToString()))
                    {
                        landmark = val.ToString();
                        break;
                    }
                }

                string city = null;
                foreach (var field in _cityFields)
                {
                    var val = address[field];
                    if (val != null && !string.IsNullOrEmpty(val.ToString()))
                    {
                        city = val.ToString();
                        break;
                    }
                }

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

            return new GeocodingResult
            {
                PlaceName = null,
                FullResponse = response
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Geocoding failed for lat={Lat}, lon={Lon}", latitude, longitude);
            return null;
        }
    }
}
