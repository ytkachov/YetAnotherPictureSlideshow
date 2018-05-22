using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace weather
{
  public enum WeatherPeriod
  {
    Undefined,
    Now,
    TodayMorning,
    TodayDay,
    TodayEvening,
    TodayNight,
    TomorrowMorning,
    TomorrowDay,
    TomorrowEvening,
    TomorrowNight,
    DayAfterTomorrowMorning,
    DayAfterTomorrowDay,
    DayAfterTomorrowEvening,
    DayAfterTomorrowNight
  }

  public enum WindDirection
  {
    Undefined,
    N,
    NNE,
    NE,
    ENE,
    E,
    ESE,
    SE,
    SSE,
    S,
    SSW,
    SW,
    WSW,
    W,
    WNW,
    NW,
    NNW
  }

  public enum WeatherType
  {
    Undefined,
    Clear,                  // ясно
    ClearPartlyRainy,       // ясно, временами небольшой дождь
    ClearPartlySnowy,       // ясно, временами небольшой снег
    ClearRainy,             // ясно, дождь
    PartlyCloudy,           // + легкая облачность
    Cloudy,                 // + облачно с прояснениями
    CloudyPartlyRainy,      // + облачно, небольшой дождь
    CloudyPartlySnowy,      // + облачно, небольшой снег
    CloudyRainy,            // + облачность, дождь
    CloudySnowy,            // + облачность, снег 
    CloudyRainyStorm,       // + ливень  
    CloudySnowyStorm,       // + сильный снег
    CloudyLightningRainy,   // облачно, гроза
    Overcast,               // + пасмурно
    OvercastPartlyRainy,    // + пасмурно, временами дождь
    OvercastPartlySnowy,    // + пасмурно, временами снег
    OvercastRainy,          // + пасмурно, дождь
    OvercastSnowy,          // + пасмурно, снег
    OvercastLightningRainy, // + пасмурно, дождь, гроза
    OvercastRainyStorm,     // + вообще ливень
    OvercastSnowyStorm,     // + вообще снежище
    Blizzard,               // — метель
    Fog                     // — туман
  }

  public interface IWeatherProvider
  {
    bool get_temperature(WeatherPeriod time, out double temp_low, out double temp_high);
    bool get_pressure(WeatherPeriod time, out double pressure);
    bool get_humidity(WeatherPeriod time, out double hum);
    bool get_wind(WeatherPeriod time, out WindDirection direction, out double speed);
    bool get_character(WeatherPeriod time, out WeatherType type);
    string get_error_description();
    int release();
  }
}
