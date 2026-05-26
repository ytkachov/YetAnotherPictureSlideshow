using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Yaps.Core.Models.Weather;

namespace Yaps.Infrastructure.Weather.Parsing;

/// <summary>
/// Wind-direction strings each provider site uses. Lookups are
/// case-sensitive on purpose — provider sites rarely change letter
/// casing in their markup, and being strict surfaces a real format
/// drift instead of silently misclassifying.
/// </summary>
public static class WindDirectionMap
{
    // Yandex API ("wind_dir" field) — lowercase NESW combinations.
    public static readonly FrozenDictionary<string, WindDirection> YandexApi = new Dictionary<string, WindDirection>
    {
        ["n"]  = WindDirection.N,
        ["e"]  = WindDirection.E,
        ["s"]  = WindDirection.S,
        ["w"]  = WindDirection.W,
        ["ne"] = WindDirection.NE,
        ["nw"] = WindDirection.NW,
        ["se"] = WindDirection.SE,
        ["sw"] = WindDirection.SW
    }.ToFrozenDictionary();

    // Yandex Pogoda HTML — Cyrillic single-letter abbreviations.
    public static readonly FrozenDictionary<string, WindDirection> YandexHtml = new Dictionary<string, WindDirection>
    {
        ["С"]  = WindDirection.N,
        ["В"]  = WindDirection.E,
        ["Ю"]  = WindDirection.S,
        ["З"]  = WindDirection.W,
        ["СВ"] = WindDirection.NE,
        ["СЗ"] = WindDirection.NW,
        ["ЮВ"] = WindDirection.SE,
        ["ЮЗ"] = WindDirection.SW
    }.ToFrozenDictionary();

    // NGS Pogoda HTML — CSS class suffix on icon-wind-* spans.
    public static readonly FrozenDictionary<string, WindDirection> Ngs = new Dictionary<string, WindDirection>
    {
        ["north"]      = WindDirection.N,
        ["north_east"] = WindDirection.NE,
        ["east"]       = WindDirection.E,
        ["south_east"] = WindDirection.SE,
        ["south"]      = WindDirection.S,
        ["south_west"] = WindDirection.SW,
        ["west"]       = WindDirection.W,
        ["north_west"] = WindDirection.NW
    }.ToFrozenDictionary();

    public static WindDirection Lookup(FrozenDictionary<string, WindDirection> map, string? key)
        => key is not null && map.TryGetValue(key, out var dir) ? dir : WindDirection.Undefined;

    // 16-point compass slices of 22.5°; the +11.25 offset centres each
    // slice on its bearing (N covers 348.75°-11.25°, NNE 11.25°-33.75°, …).
    // Open-Meteo reports wind_direction in meteorological degrees (0 = wind
    // from the north). Anything outside the [0, 360) range or non-finite
    // collapses to Undefined.
    private static readonly WindDirection[] _compass16 =
    {
        WindDirection.N,   WindDirection.NNE, WindDirection.NE,  WindDirection.ENE,
        WindDirection.E,   WindDirection.ESE, WindDirection.SE,  WindDirection.SSE,
        WindDirection.S,   WindDirection.SSW, WindDirection.SW,  WindDirection.WSW,
        WindDirection.W,   WindDirection.WNW, WindDirection.NW,  WindDirection.NNW
    };

    public static WindDirection FromDegrees(double? degrees)
    {
        if (degrees is not double d || double.IsNaN(d) || double.IsInfinity(d))
            return WindDirection.Undefined;

        // Normalise into [0, 360) without modulo on a negative double.
        d = ((d % 360.0) + 360.0) % 360.0;
        int index = (int)Math.Floor((d + 11.25) / 22.5) % 16;
        return _compass16[index];
    }
}
