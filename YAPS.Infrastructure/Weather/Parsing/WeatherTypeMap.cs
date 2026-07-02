using System.Collections.Frozen;
using System.Collections.Generic;
using Yaps.Core.Models.Weather;

namespace Yaps.Infrastructure.Weather.Parsing;

/// <summary>
/// Per-provider lookup from raw site token (icon-class suffix, JSON
/// "icon" field, etc.) to our canonical <see cref="WeatherType"/>.
/// Each provider site uses its own vocabulary — there's no winning by
/// flattening these into a single shared table.
/// </summary>
public static class WeatherTypeMap
{
    public static readonly FrozenDictionary<string, WeatherType> YandexApi = new Dictionary<string, WeatherType>
    {
        ["skc_n"]      = WeatherType.Clear,
        ["skc_d"]      = WeatherType.Clear,
        ["bkn_d"]      = WeatherType.Cloudy,
        ["bkn_n"]      = WeatherType.Cloudy,
        ["bkn_-ra_d"]  = WeatherType.CloudyPartlyRainy,
        ["bkn_-ra_n"]  = WeatherType.CloudyPartlyRainy,
        ["bkn_-sn_d"]  = WeatherType.CloudyPartlySnowy,
        ["bkn_-sn_n"]  = WeatherType.CloudyPartlySnowy,
        ["bkn_ra_d"]   = WeatherType.CloudyRainy,
        ["bkn_ra_n"]   = WeatherType.CloudyRainy,
        ["bkn_sn_d"]   = WeatherType.CloudySnowy,
        ["bkn_sn_n"]   = WeatherType.CloudySnowy,
        ["bkn_+ra_d"]  = WeatherType.CloudyRainyStorm,
        ["bkn_+ra_n"]  = WeatherType.CloudyRainyStorm,
        ["bkn_+sn_d"]  = WeatherType.CloudySnowyStorm,
        ["bkn_+sn_n"]  = WeatherType.CloudySnowyStorm,
        ["-bl"]        = WeatherType.Blizzard,
        ["bl"]         = WeatherType.Blizzard,
        ["fg_d"]       = WeatherType.Fog,
        ["fg_n"]       = WeatherType.Fog,
        ["ovc"]        = WeatherType.Overcast,
        ["ovc_ha"]     = WeatherType.Overcast,
        ["ovc_-ra"]    = WeatherType.OvercastPartlyRainy,
        ["ovc_-sn"]    = WeatherType.OvercastPartlySnowy,
        ["ovc_ra"]     = WeatherType.OvercastRainy,
        ["ovc_sn"]     = WeatherType.OvercastSnowy,
        ["ovc_+ra"]    = WeatherType.OvercastRainyStorm,
        ["ovc_+sn"]    = WeatherType.OvercastSnowyStorm,
        ["ovc_ra_sn"]  = WeatherType.OvercastSnowyStorm,
        ["ovc_ts"]     = WeatherType.OvercastLightningRainy,
        ["ovc_ts_ra"]  = WeatherType.OvercastLightningRainy,
        ["ovc_ts_ha"]  = WeatherType.OvercastLightningRainy
    }.ToFrozenDictionary();

    // WMO weather codes (Open-Meteo's `weather_code`). Day/night doesn't
    // matter — Open-Meteo doesn't split icons by time of day, so a single
    // table is enough. Codes that we don't have a dedicated glyph for
    // collapse to the nearest visual equivalent (e.g. freezing drizzle →
    // partly-rainy because the slideshow has no ice glyph).
    public static readonly FrozenDictionary<int, WeatherType> OpenMeteoWmo = new Dictionary<int, WeatherType>
    {
        [0]  = WeatherType.Clear,
        [1]  = WeatherType.Clear,
        [2]  = WeatherType.PartlyCloudy,
        [3]  = WeatherType.Overcast,
        [45] = WeatherType.Fog,
        [48] = WeatherType.Fog,
        [51] = WeatherType.OvercastPartlyRainy,
        [53] = WeatherType.OvercastPartlyRainy,
        [55] = WeatherType.OvercastRainy,
        [56] = WeatherType.OvercastPartlyRainy,
        [57] = WeatherType.OvercastRainy,
        [61] = WeatherType.OvercastPartlyRainy,
        [63] = WeatherType.OvercastRainy,
        [65] = WeatherType.OvercastRainyStorm,
        [66] = WeatherType.OvercastRainy,
        [67] = WeatherType.OvercastRainyStorm,
        [71] = WeatherType.OvercastPartlySnowy,
        [73] = WeatherType.OvercastSnowy,
        [75] = WeatherType.OvercastSnowyStorm,
        [77] = WeatherType.OvercastPartlySnowy,
        [80] = WeatherType.CloudyPartlyRainy,
        [81] = WeatherType.CloudyRainy,
        [82] = WeatherType.CloudyRainyStorm,
        [85] = WeatherType.CloudyPartlySnowy,
        [86] = WeatherType.CloudySnowy,
        [95] = WeatherType.OvercastLightningRainy,
        [96] = WeatherType.OvercastLightningRainy,
        [99] = WeatherType.OvercastLightningRainy
    }.ToFrozenDictionary();

    public static WeatherType Lookup(FrozenDictionary<string, WeatherType> map, string? key)
        => key is not null && map.TryGetValue(key, out var t) ? t : WeatherType.Undefined;

    public static WeatherType Lookup(FrozenDictionary<int, WeatherType> map, int? key)
        => key is int k && map.TryGetValue(k, out var t) ? t : WeatherType.Undefined;
}
