using System;
using System.Collections.Generic;
using System.Linq;

using System.Xml;

namespace weather
{
  class WeatherProviderYandex : WeatherProviderBase
  {
    static Dictionary<string, WindDirection> wind_direction_encoding = new Dictionary<string, WindDirection>()
      { { "e", WindDirection.E }, { "ne", WindDirection.NE }, { "n", WindDirection.N }, { "nw", WindDirection.NW }, { "w", WindDirection.W }, { "sw", WindDirection.SW }, { "s", WindDirection.S }, { "se", WindDirection.SE }};

    static Dictionary<string, WeatherType> weather_type_encoding = new Dictionary<string, WeatherType>()
    {
      { "bkn_-ra_d",  WeatherType.CloudyPartlyRainy },      // — облачно с прояснениями, небольшой дождь (день)
      { "bkn_-ra_n",  WeatherType.CloudyPartlyRainy },      // — облачно с прояснениями, небольшой дождь (ночь)
      { "bkn_-sn_d",  WeatherType.CloudyPartlySnowy },      // — облачно с прояснениями, небольшой снег (день)
      { "bkn_-sn_n",  WeatherType.CloudyPartlySnowy },      // — облачно с прояснениями, небольшой снег (ночь)
      { "bkn_d",      WeatherType.Cloudy },                 // — переменная облачность (день)
      { "bkn_n",      WeatherType.Cloudy },                 // — переменная облачность (ночь)
      { "bkn_ra_d",   WeatherType.CloudyRainy },            // — переменная облачность, дождь (день)
      { "bkn_ra_n",   WeatherType.CloudyRainy },            // — переменная облачность, дождь (ночь)
      { "bkn_sn_d",   WeatherType.CloudySnowy },            // — переменная облачность, снег (день)
      { "bkn_sn_n",   WeatherType.CloudySnowy },            // — переменная облачность, снег (ночь)
      { "bl",         WeatherType.Blizzard },               // — метель
      { "fg_d",       WeatherType.Fog },                    // — туман
      { "ovc",        WeatherType.Overcast },               // — облачно
      { "ovc_-ra",    WeatherType.OvercastPartlyRainy },    // — облачно, временами дождь
      { "ovc_-sn",    WeatherType.OvercastPartlySnowy },    // — облачно, временами снег
      { "ovc_ra",     WeatherType.OvercastRainy },          // — облачно, дождь
      { "ovc_sn",     WeatherType.OvercastSnowy },          // — облачно, снег
      { "ovc_ts_ra",  WeatherType.OvercastLightningRainy }, //  — облачно, дождь, гроза
      { "skc_d",      WeatherType.Clear },                  // ясно (день)
      { "skc_n",      WeatherType.Clear },                  // ясно (ночь)
      { "bkn_+ra_d",  WeatherType.CloudyRainyStorm },
      { "bkn_+ra_n",  WeatherType.CloudyRainyStorm },
      { "bkn_+sn_d",  WeatherType.CloudySnowyStorm },
      { "bkn_+sn_n",  WeatherType.CloudySnowyStorm },
      { "ovc_+ra",    WeatherType.OvercastRainyStorm },
      { "ovc_+sn",    WeatherType.OvercastRainyStorm }
    };

    private static IWeatherProvider _self = null;
    private static int _refcounter = 0;
    private IWeatherReader _sitereader = null;

    private WeatherProviderYandex(IWeatherReader reader)
    {
      if (reader != null)
        _sitereader = reader;
      else 
        _sitereader = new YandexFileReaderWriter(WeatherSource.NC);
    }

    public static IWeatherProvider get(IWeatherReader reader = null)
    {
      if (_self == null)
        _self = new WeatherProviderYandex(reader);

      _refcounter++;
      return _self;
    }

    public override int release()
    {
      if (--_refcounter == 0)
        close();

      return _refcounter;
    }

    protected override void read_current_weather() 
    { 
    }
    
    protected override void read_forecast() 
    { 
    }
  }

}
