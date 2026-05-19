using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Yaps.Infrastructure.Weather.Parsing.YandexApi;

/// <summary>
/// System.Text.Json-shaped mirror of the v2/forecast response. Replaces
/// the DataContract+Newtonsoft pair the legacy library used; the JSON
/// names are pinned by attribute so renames don't drift away from the
/// Yandex schema. Only the fields the screensaver actually consumes are
/// modelled — everything else is silently ignored (default STJ behaviour).
/// </summary>
public sealed class YandexApiResponse
{
    [JsonPropertyName("info")]
    public YandexApiInfo? Info { get; set; }

    [JsonPropertyName("fact")]
    public YandexApiFact? Fact { get; set; }

    [JsonPropertyName("forecasts")]
    public List<YandexApiForecast>? Forecasts { get; set; }
}

public sealed class YandexApiInfo
{
    [JsonPropertyName("def_pressure_mm")]
    public double? DefPressureMm { get; set; }
}

public sealed class YandexApiFact
{
    [JsonPropertyName("temp")]
    public double? Temp { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("wind_speed")]
    public double? WindSpeed { get; set; }

    [JsonPropertyName("wind_dir")]
    public string? WindDir { get; set; }

    [JsonPropertyName("pressure_mm")]
    public double? PressureMm { get; set; }

    [JsonPropertyName("humidity")]
    public double? Humidity { get; set; }
}

public sealed class YandexApiForecast
{
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("parts")]
    public YandexApiParts? Parts { get; set; }
}

public sealed class YandexApiParts
{
    [JsonPropertyName("morning")]
    public YandexApiDayPart? Morning { get; set; }

    [JsonPropertyName("day")]
    public YandexApiDayPart? Day { get; set; }

    [JsonPropertyName("evening")]
    public YandexApiDayPart? Evening { get; set; }

    [JsonPropertyName("night")]
    public YandexApiDayPart? Night { get; set; }
}

public sealed class YandexApiDayPart
{
    [JsonPropertyName("temp_min")]
    public double? TempMin { get; set; }

    [JsonPropertyName("temp_max")]
    public double? TempMax { get; set; }

    [JsonPropertyName("temp_avg")]
    public double? TempAvg { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("wind_speed")]
    public double? WindSpeed { get; set; }

    [JsonPropertyName("wind_dir")]
    public string? WindDir { get; set; }

    [JsonPropertyName("pressure_mm")]
    public double? PressureMm { get; set; }

    [JsonPropertyName("humidity")]
    public double? Humidity { get; set; }
}

/// <summary>
/// Source-generated metadata for <see cref="YandexApiResponse"/> so the
/// HTTP path stays reflection-free and AOT-tolerant.
/// </summary>
[JsonSerializable(typeof(YandexApiResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
public partial class YandexApiJsonContext : JsonSerializerContext
{
}
