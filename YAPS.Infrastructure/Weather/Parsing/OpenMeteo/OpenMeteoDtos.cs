using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Yaps.Infrastructure.Weather.Parsing.OpenMeteo;

/// <summary>
/// System.Text.Json-shaped mirror of the Open-Meteo /v1/forecast response.
/// Hourly data is delivered as parallel arrays (one entry per ISO-local
/// timestamp), the provider walks them by index to bucket into our
/// morning/day/evening/night periods.
/// </summary>
public sealed class OpenMeteoResponse
{
    [JsonPropertyName("utc_offset_seconds")]
    public int? UtcOffsetSeconds { get; set; }

    [JsonPropertyName("current")]
    public OpenMeteoCurrent? Current { get; set; }

    [JsonPropertyName("hourly")]
    public OpenMeteoHourly? Hourly { get; set; }
}

public sealed class OpenMeteoCurrent
{
    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("temperature_2m")]
    public double? Temperature2m { get; set; }

    [JsonPropertyName("weather_code")]
    public int? WeatherCode { get; set; }

    [JsonPropertyName("wind_speed_10m")]
    public double? WindSpeed10m { get; set; }

    [JsonPropertyName("wind_direction_10m")]
    public double? WindDirection10m { get; set; }

    [JsonPropertyName("surface_pressure")]
    public double? SurfacePressureHpa { get; set; }

    [JsonPropertyName("relative_humidity_2m")]
    public double? RelativeHumidity2m { get; set; }
}

public sealed class OpenMeteoHourly
{
    [JsonPropertyName("time")]
    public List<string>? Time { get; set; }

    [JsonPropertyName("temperature_2m")]
    public List<double?>? Temperature2m { get; set; }

    [JsonPropertyName("weather_code")]
    public List<int?>? WeatherCode { get; set; }

    [JsonPropertyName("wind_speed_10m")]
    public List<double?>? WindSpeed10m { get; set; }

    [JsonPropertyName("wind_direction_10m")]
    public List<double?>? WindDirection10m { get; set; }

    [JsonPropertyName("surface_pressure")]
    public List<double?>? SurfacePressureHpa { get; set; }

    [JsonPropertyName("relative_humidity_2m")]
    public List<double?>? RelativeHumidity2m { get; set; }
}

/// <summary>
/// Source-generated metadata for <see cref="OpenMeteoResponse"/> so the
/// HTTP path stays reflection-free and AOT-tolerant.
/// </summary>
[JsonSerializable(typeof(OpenMeteoResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
public partial class OpenMeteoJsonContext : JsonSerializerContext
{
}
