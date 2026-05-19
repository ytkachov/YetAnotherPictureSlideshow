namespace Yaps.Core.Models.Weather;

/// <summary>
/// Weather "characters" the WPF SVG resource map keys off. Adding new
/// values without a matching glyph in WeatherFormatter.weather_types_to_picture
/// will surface as an undefined-icon fallback at render time, not a build break.
/// </summary>
public enum WeatherType
{
    Undefined,
    Clear,                  // ясно
    ClearPartlyRainy,       // ясно, временами небольшой дождь
    ClearPartlySnowy,       // ясно, временами небольшой снег
    ClearRainy,             // ясно, дождь
    PartlyCloudy,           // легкая облачность
    Cloudy,                 // облачно с прояснениями
    CloudyPartlyRainy,      // облачно, небольшой дождь
    CloudyPartlySnowy,      // облачно, небольшой снег
    CloudyRainy,            // облачность, дождь
    CloudySnowy,            // облачность, снег
    CloudyRainyStorm,       // ливень
    CloudySnowyStorm,       // сильный снег
    CloudyLightningRainy,   // облачно, гроза
    Overcast,               // пасмурно
    OvercastPartlyRainy,    // пасмурно, временами дождь
    OvercastPartlySnowy,    // пасмурно, временами снег
    OvercastRainy,          // пасмурно, дождь
    OvercastSnowy,          // пасмурно, снег
    OvercastLightningRainy, // пасмурно, дождь, гроза
    OvercastRainyStorm,     // ливень
    OvercastSnowyStorm,     // снежище
    Blizzard,               // метель
    Fog                     // туман
}
