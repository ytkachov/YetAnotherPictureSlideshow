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

    public static readonly FrozenDictionary<string, WeatherType> YandexHtml = new Dictionary<string, WeatherType>
    {
        ["bkn-d"]      = WeatherType.Cloudy,
        ["bkn-n"]      = WeatherType.Cloudy,
        ["bkn-m-sn-d"] = WeatherType.CloudyPartlySnowy,
        ["bkn-m-sn-n"] = WeatherType.CloudyPartlySnowy,
        ["bkn-m-ra-d"] = WeatherType.CloudyPartlyRainy,
        ["bkn-m-ra-n"] = WeatherType.CloudyPartlyRainy,
        ["bkn-ra-d"]   = WeatherType.CloudyRainy,
        ["bkn-ra-n"]   = WeatherType.CloudyRainy,
        ["bkn-sn-d"]   = WeatherType.CloudySnowy,
        ["bkn-sn-n"]   = WeatherType.CloudySnowy,
        ["bkn-p-ra-n"] = WeatherType.CloudyRainyStorm,
        ["bkn-p-ra-d"] = WeatherType.CloudyRainyStorm,
        ["bkn-p-sn-n"] = WeatherType.CloudySnowyStorm,
        ["bkn-p-sn-d"] = WeatherType.CloudySnowyStorm,
        ["bl"]         = WeatherType.Blizzard,
        ["fg-d"]       = WeatherType.Fog,
        ["fg-n"]       = WeatherType.Fog,
        ["ovc"]        = WeatherType.Overcast,
        ["ovc-m-ra"]   = WeatherType.OvercastPartlyRainy,
        ["ovc-m-sn"]   = WeatherType.OvercastPartlySnowy,
        ["ovc-ra"]     = WeatherType.OvercastRainy,
        ["ovc-sn"]     = WeatherType.OvercastSnowy,
        ["ovc-p-ra"]   = WeatherType.OvercastRainyStorm,
        ["ovc-p-sn"]   = WeatherType.OvercastSnowyStorm,
        ["ovc-ra-sn"]  = WeatherType.OvercastSnowy,
        ["ovc-ts-ra"]  = WeatherType.OvercastLightningRainy,
        ["skc-n"]      = WeatherType.Clear,
        ["skc-d"]      = WeatherType.Clear
    }.ToFrozenDictionary();

    public static readonly FrozenDictionary<string, WeatherType> Ngs = new Dictionary<string, WeatherType>
    {
        ["sunshine_light_rain_day"]          = WeatherType.ClearPartlyRainy,
        ["sunshine_light_snow_day"]          = WeatherType.ClearPartlySnowy,
        ["sunshine_rain_day"]                = WeatherType.ClearRainy,
        ["sunshine_none_day"]                = WeatherType.Clear,
        ["partly_cloudy_rain_day"]           = WeatherType.CloudyRainy,
        ["partly_cloudy_light_rain_day"]     = WeatherType.CloudyPartlyRainy,
        ["partly_cloudy_rain_with_snow_day"] = WeatherType.CloudyPartlyRainy,
        ["partly_cloudy_snow_day"]           = WeatherType.CloudySnowy,
        ["partly_cloudy_light_snow_day"]     = WeatherType.CloudyPartlySnowy,
        ["partly_cloudy_thunderstorm_day"]   = WeatherType.CloudyLightningRainy,
        ["light_cloudy_none_day"]            = WeatherType.PartlyCloudy,
        ["partly_cloudy_none_day"]           = WeatherType.PartlyCloudy,
        ["partly_cloudy_rainless_day"]       = WeatherType.PartlyCloudy,
        ["mostly_cloudy_rain_day"]           = WeatherType.CloudyRainy,
        ["mostly_cloudy_light_rain_day"]     = WeatherType.CloudyPartlyRainy,
        ["mostly_cloudy_snow_day"]           = WeatherType.CloudySnowy,
        ["mostly_cloudy_light_snow_day"]     = WeatherType.CloudyPartlySnowy,
        ["mostly_cloudy_thunderstorm_day"]   = WeatherType.CloudyLightningRainy,
        ["mostly_cloudy_none_day"]           = WeatherType.Cloudy,
        ["mostly_cloudy_sleet_day"]          = WeatherType.OvercastPartlySnowy,
        ["cloudy_rain_day"]                  = WeatherType.OvercastRainy,
        ["cloudy_rainless_day"]              = WeatherType.Overcast,
        ["cloudy_light_rain_day"]            = WeatherType.OvercastPartlyRainy,
        ["cloudy_sleet_day"]                 = WeatherType.OvercastPartlyRainy,
        ["cloudy_snow_with_rain_day"]        = WeatherType.OvercastPartlyRainy,
        ["cloudy_snow_day"]                  = WeatherType.OvercastSnowy,
        ["cloudy_light_snow_day"]            = WeatherType.OvercastPartlySnowy,
        ["cloudy_heavy_snow_day"]            = WeatherType.OvercastSnowyStorm,
        ["cloudy_thunderstorm_day"]          = WeatherType.OvercastLightningRainy,
        ["cloudy_none_day"]                  = WeatherType.Overcast,
        ["sunshine_light_rain_night"]          = WeatherType.ClearPartlyRainy,
        ["sunshine_light_snow_night"]          = WeatherType.ClearPartlySnowy,
        ["sunshine_rain_night"]                = WeatherType.ClearRainy,
        ["sunshine_none_night"]                = WeatherType.Clear,
        ["partly_cloudy_rain_night"]           = WeatherType.CloudyRainy,
        ["partly_cloudy_light_rain_night"]     = WeatherType.CloudyPartlyRainy,
        ["partly_cloudy_rain_with_snow_night"] = WeatherType.CloudyPartlyRainy,
        ["partly_cloudy_snow_night"]           = WeatherType.CloudySnowy,
        ["partly_cloudy_light_snow_night"]     = WeatherType.CloudyPartlySnowy,
        ["partly_cloudy_thunderstorm_night"]   = WeatherType.CloudyLightningRainy,
        ["light_cloudy_none_night"]            = WeatherType.PartlyCloudy,
        ["partly_cloudy_none_night"]           = WeatherType.PartlyCloudy,
        ["partly_cloudy_rainless_night"]       = WeatherType.PartlyCloudy,
        ["mostly_cloudy_rain_night"]           = WeatherType.CloudyRainy,
        ["mostly_cloudy_light_rain_night"]     = WeatherType.CloudyPartlyRainy,
        ["mostly_cloudy_snow_night"]           = WeatherType.CloudySnowy,
        ["mostly_cloudy_light_snow_night"]     = WeatherType.CloudyPartlySnowy,
        ["mostly_cloudy_thunderstorm_night"]   = WeatherType.CloudyLightningRainy,
        ["mostly_cloudy_none_night"]           = WeatherType.Cloudy,
        ["mostly_cloudy_sleet_night"]          = WeatherType.OvercastPartlySnowy,
        ["cloudy_rain_night"]                  = WeatherType.OvercastRainy,
        ["cloudy_rainless_night"]              = WeatherType.Overcast,
        ["cloudy_light_rain_night"]            = WeatherType.OvercastPartlyRainy,
        ["cloudy_sleet_night"]                 = WeatherType.OvercastPartlyRainy,
        ["cloudy_snow_with_rain_night"]        = WeatherType.OvercastPartlyRainy,
        ["cloudy_snow_night"]                  = WeatherType.OvercastSnowy,
        ["cloudy_light_snow_night"]            = WeatherType.OvercastPartlySnowy,
        ["cloudy_heavy_snow_night"]            = WeatherType.OvercastSnowyStorm,
        ["cloudy_thunderstorm_night"]          = WeatherType.OvercastLightningRainy,
        ["cloudy_none_night"]                  = WeatherType.Overcast
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
