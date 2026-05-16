using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

using System.Xml;
using System.Xml.Serialization;
using Newtonsoft.Json;
using Serilog;

namespace weather
{
  public class YandexWeatherApiExtractor
  {
    static WeatherPeriod[] day_periods = new WeatherPeriod[]
    {
      WeatherPeriod.TodayMorning,            WeatherPeriod.TodayDay,            WeatherPeriod.TodayEvening,            WeatherPeriod.TodayNight,
      WeatherPeriod.TomorrowMorning,         WeatherPeriod.TomorrowDay,         WeatherPeriod.TomorrowEvening,         WeatherPeriod.TomorrowNight
    };

    static readonly FrozenDictionary<string, WindDirection> wind_direction_encoding = new Dictionary<string, WindDirection>()
      {
        { "n", WindDirection.N },
        { "e", WindDirection.E },
        { "s", WindDirection.S },
        { "w", WindDirection.W },
        { "ne", WindDirection.NE },
        { "nw", WindDirection.NW },
        { "se", WindDirection.SE },
        { "sw", WindDirection.SW }
    }.ToFrozenDictionary();

    static readonly FrozenDictionary<string, WeatherType> weather_type_encoding = new Dictionary<string, WeatherType>()
    {
      { "skc_n",        WeatherType.Clear }, // - Малооблачно
      { "skc_d",        WeatherType.Clear }, // - Ясно

      { "bkn_d",        WeatherType.Cloudy }, // - облачно с прояснениями
      { "bkn_n",        WeatherType.Cloudy }, // - облачно с прояснениями

      { "bkn_-ra_d",    WeatherType.CloudyPartlyRainy }, // - Небольшой дождь
      { "bkn_-ra_n",    WeatherType.CloudyPartlyRainy }, // - Небольшой дождь
      { "bkn_-sn_d",    WeatherType.CloudyPartlySnowy }, // - небольшой снег
      { "bkn_-sn_n",    WeatherType.CloudyPartlySnowy }, // - Небольшой снег

      { "bkn_ra_d",     WeatherType.CloudyRainy }, // - Дождь
      { "bkn_ra_n",     WeatherType.CloudyRainy }, // - Дождь
      { "bkn_sn_d",     WeatherType.CloudySnowy }, // - Снег
      { "bkn_sn_n",     WeatherType.CloudySnowy }, // - Снег

      { "bkn_+ra_d",    WeatherType.CloudyRainyStorm }, // - Ливень
      { "bkn_+ra_n",    WeatherType.CloudyRainyStorm }, // - Ливень
      { "bkn_+sn_d",    WeatherType.CloudySnowyStorm }, // - сильный Снег
      { "bkn_+sn_n",    WeatherType.CloudySnowyStorm }, // - сильный Снег

      { "-bl",          WeatherType.Blizzard },         // — слабая метель
      { "bl",           WeatherType.Blizzard },          // — метель
      { "fg_d",         WeatherType.Fog },                    // — туман
      { "fg_n",         WeatherType.Fog },                    // — туман

      { "ovc",          WeatherType.Overcast },               // — пасмурно
      { "ovc_ha",       WeatherType.Overcast },               // — пасмурно, град

      { "ovc_-ra",      WeatherType.OvercastPartlyRainy }, // - небольшой дождь
      { "ovc_-sn",      WeatherType.OvercastPartlySnowy }, // - небольшой снег

      { "ovc_ra",       WeatherType.OvercastRainy }, // - дождь
      { "ovc_sn",       WeatherType.OvercastSnowy }, // - Снег
      
      { "ovc_+ra",      WeatherType.OvercastRainyStorm },   // - ливень
      { "ovc_+sn",      WeatherType.OvercastSnowyStorm },   // - сильный снег
      { "ovc_ra_sn",    WeatherType.OvercastSnowyStorm }, // - пасмутно, снег с дождем

      { "ovc_ts",       WeatherType.OvercastLightningRainy }, //  — облачно, гроза
      { "ovc_ts_ra",    WeatherType.OvercastLightningRainy }, //  — облачно, дождь, гроза
      { "ovc_ts_ha",    WeatherType.OvercastLightningRainy }  //  — облачно, град, гроза

    }.ToFrozenDictionary();

    private IWeatherReader _sitereader = null;

    public YandexWeatherApiExtractor(IWeatherReader reader)
    {
      if (reader != null)
        _sitereader = reader;
      else
        _sitereader = new YandexApiReaderWriter();
    }

    public void get_current_weather(WeatherInfo w)
    {
      string current = _sitereader.current();
      if (string.IsNullOrEmpty(current))
        throw new Exception("incorrect current weather structure ");

      // No $type allowed: the JSON shape is fixed by YandexWeatherFact's
      // DataContract attributes.
      YandexWeatherFact fact = JsonConvert.DeserializeObject<YandexWeatherFact>(current);

      // weather character
      string wt = fact.Icon;
      w.WeatherType = weather_type_encoding.Keys.Contains(wt) ? weather_type_encoding[wt] : WeatherType.Undefined;

      // air temperature
      w.TemperatureHigh = w.TemperatureLow = fact.Temp;

      // wind 
      w.WindSpeed = fact.WindSpeed;
      w.WindDirection = WindDirection.Undefined;
      if (wind_direction_encoding.Keys.Contains(fact.WindDir))
        w.WindDirection = wind_direction_encoding[fact.WindDir];

      // humidity
      w.Humidity = fact.Humidity;

      // pressure 
      w.Pressure = fact.PressureMm;

    }

    internal void get_nsu_current_temp(WeatherInfo w)
    {
      string st = _sitereader.temperature();
      // Was || which dereferenced st when null; need && so we only parse a
      // non-null string that actually contains the degree sign.
      if (st != null && st.Contains("°"))
      {
        CultureInfo culture = new CultureInfo("en");
        double t = double.Parse(st.Substring(0, st.IndexOf("°")), culture);
        w.TemperatureLow = w.TemperatureHigh = t;
      }
      else
        throw new Exception("incorrect NSU current temperature");
    }

    public void get_forecast(Dictionary<WeatherPeriod, WeatherInfo> weather)
    {
      string forecast = _sitereader.forecast();
      if (string.IsNullOrEmpty(forecast))
        throw new Exception("incorrect current weather structure ");

      XmlDocument pg = new XmlDocument();
      pg.LoadXml(forecast);

      XmlNode pgd_forecast = pg.DocumentElement;
      var day_divs = pgd_forecast.SelectNodes("//div[@class = 'card']");
      if (day_divs.Count == 0)
        day_divs = pgd_forecast.SelectNodes("//article[@class = 'card']");

      int day_period = 0;
      for (int day = 0; day < day_divs.Count; day++)
      {
        XmlNode day_div = day_divs[day];

        var table_rows = day_div.SelectNodes("./dd[@class='forecast-details__day-info']/table[@class = 'weather-table']/tbody[@class = 'weather-table__body']/tr[@class = 'weather-table__row']");
        if (table_rows.Count == 0)
          table_rows = day_div.SelectNodes("./div[@class='forecast-details__day-info']/table[@class = 'weather-table']/tbody[@class = 'weather-table__body']/tr[@class = 'weather-table__row']");

        for (int period = 0; period < 4; period++) // утро, день, вечер, ночь
        {
          XmlNode row = table_rows[period];
          WeatherInfo w = new WeatherInfo();

          get_daypart_weather(row, w);
          if (day_period < day_periods.Length)
            weather[day_periods[day_period]] = w;

          day_period++;
        }
      }
    }

    private void get_daypart_weather(XmlNode row, WeatherInfo w)
    {
      // type
      string wt = get_weather_type(row, "./td/img", "icon icon_thumb_");
      w.WeatherType = weather_type_encoding.Keys.Contains(wt) ? weather_type_encoding[wt] : WeatherType.Undefined;

      // temperature
      var air_temps = row.SelectNodes("./td//div/span[@class='temp__value temp__value_with-unit']");
      if (air_temps.Count >= 2)
      {
        w.TemperatureLow = get_air_temperature(air_temps[0]);
        w.TemperatureHigh = get_air_temperature(air_temps[1]);
      }
      else if (air_temps.Count == 1)
      {
        w.TemperatureLow = w.TemperatureHigh = get_air_temperature(air_temps[0]);
      }
      else
        throw new Exception("incorrect forecast structure-- cant find temperature ");

      // pressure 
      var pressure = row.SelectSingleNode("./td[@class = 'weather-table__body-cell weather-table__body-cell_type_air-pressure']");
      if (pressure == null)
        throw new Exception("incorrect forecast structure-- cant find pressure");

      if (double.TryParse(pressure.InnerText.Replace(',', '.').Replace('−', '-').Trim(),
                          NumberStyles.Number, new CultureInfo("en"), out double press))
        w.Pressure = press;

      // humidity
      var humidity = row.SelectSingleNode("./td[@class = 'weather-table__body-cell weather-table__body-cell_type_humidity']");
      if (humidity == null)
        throw new Exception("incorrect forecast structure-- cant find humidity");

      if (double.TryParse(humidity.InnerText.Replace(',', '.').Replace('−', '-').Replace('%', ' ').Trim(),
                          NumberStyles.Number, new CultureInfo("en"), out double humi))
        w.Humidity = humi;

      // wind
      w.WindDirection = WindDirection.Undefined;
      w.WindSpeed = 0.0;
      XmlNode wind_speed = row.SelectSingleNode("./td//div//span[@class = 'wind-speed']");
      if (wind_speed != null)
      {
        if (double.TryParse(wind_speed.InnerText.Replace(',', '.').Replace('−', '-').Trim(),
            NumberStyles.Number, new CultureInfo("en"), out double windspeed))
          w.WindSpeed = windspeed;

        XmlNode wind_dir = row.SelectSingleNode($"./td//div[@class = 'weather-table__wind-direction']/abbr");
        if (wind_dir == null)
          throw new Exception("incorrect current weather structure -- cant find wind direction");

        string wind_dir_name = wind_dir.InnerText;
        if (wind_direction_encoding.Keys.Contains(wind_dir_name))
          w.WindDirection = wind_direction_encoding[wind_dir_name];
      }
      else
      {
        // may be it is штиль?
        wind_speed = row.SelectSingleNode("./td//div//span[@class = 'weather-table__wind']");
        if (wind_speed == null || !wind_speed.InnerText.Equals("Штиль"))
          throw new Exception("incorrect forecast structure -- cant find wind speed");
      }
    }

    private static string get_weather_type(XmlNode pgd_current, string node_selector, string class_name = "icon icon_color_")
    {
      XmlNode icon_weather = pgd_current.SelectSingleNode($"{node_selector}[starts-with(@class, '{class_name}')]");
      if (icon_weather == null)
        throw new Exception("incorrect current weather structure-- cant find weather type icon");

      string substr = "icon_thumb_";
      string wt = icon_weather.SelectSingleNode("@class").Value;
      int idxstart = wt.IndexOf(substr);
      if (idxstart < 0)
        throw new Exception($"incorrect current weather structure-- cant recognize weather type icon [{wt}]");

      int idxend = wt.IndexOf(' ', idxstart);
      wt = wt.Substring(idxstart + substr.Length, idxend - idxstart - substr.Length);

      return wt;
    }

    private static double? get_air_temperature(XmlNode pgd_node, string node_selector = null)
    {
      XmlNode air_temp;
      if (string.IsNullOrEmpty(node_selector))
        air_temp = pgd_node;
      else
        air_temp = pgd_node.SelectSingleNode(node_selector);

      if (air_temp == null)
        throw new Exception("incorrect current weather structure -- cant find air temperature");

      string air_temp_s = air_temp.InnerText.Replace(',', '.').Replace('−', '-').Replace('°', ' ').Trim();
      if (string.IsNullOrEmpty(air_temp_s))
        throw new Exception("incorrect current weather structure -- incorrect current temperature string");

      if (double.TryParse(air_temp_s, NumberStyles.Number, new CultureInfo("en"), out double temperature))
        return temperature;

      return null;
    }


  }

  public class WeatherProviderYandexApi : WeatherProviderBase
  {
    private static YandexWeatherApiExtractor _extractor;
    private static IWeatherProvider _self = null;
    private static int _refcounter = 0;
    private static readonly object _initLock = new object();

    private WeatherProviderYandexApi(IWeatherReader reader)
    {
      lock (_locker)
      {
        _extractor = new YandexWeatherApiExtractor(reader);
      }
    }

    public static IWeatherProvider get(IWeatherReader reader = null)
    {
      // Lock-around-create avoids two concurrent get() callers each producing
      // their own YandexApi provider and overwriting _self.
      lock (_initLock)
      {
        if (_self == null)
          _self = new WeatherProviderYandexApi(reader);

        _refcounter++;
        return _self;
      }
    }

    public override int release( )
    {
      lock (_initLock)
      {
        if (--_refcounter == 0)
          close();

        return _refcounter;
      }
    }

    protected override void read_current_weather( )
    {
      var ct = get_nsu_current_temp();
      var w = get_current_weather();

      lock (_locker)
      {
        _weather.Clear();
        if (w != null)
          _weather[WeatherPeriod.Now] = w;

        if (ct != null)
        {
          _weather[WeatherPeriod.Now].TemperatureHigh = ct.TemperatureHigh;
          _weather[WeatherPeriod.Now].TemperatureLow = ct.TemperatureLow;
        }  

      }
    }

    protected override void read_forecast( )
    {
      var weather = get_forecast();

      lock (_locker)
      {
        if (weather != null)
        {
          foreach (WeatherPeriod w in weather.Keys)
            _weather[w] = weather[w];
        }
      }
    }

    private WeatherInfo get_current_weather( )
    {
      Trace.WriteLine(">>> get_current_weather()");
      bool success = true;
      _succeeded = true;

      lock (_locker)
      {
        if (_extractor == null)
          return null;
      }

      try
      {
        var wi = new WeatherInfo();
        _extractor.get_current_weather(wi);

        return wi;

      }
      catch (Exception ex)
      {
        Log.Error(ex, "");

        success = false;
        _error_descr = ex.Message;
      }

      finally
      {
        if (!success)
        {
          lock (_locker)
          {
            _succeeded = false;
          }
        }
      }

      return null;
    }

    private Dictionary<WeatherPeriod, WeatherInfo> get_forecast( )
    {
      Trace.WriteLine(">>> get_forecast()");
      bool success = true;
      _succeeded = true;

      lock (_locker)
      {
        if (_extractor == null)
          return null;
      }

      try
      {
        Dictionary<WeatherPeriod, WeatherInfo> weather = new Dictionary<WeatherPeriod, WeatherInfo>();
        _extractor.get_forecast(weather);

        return weather;
      }
      catch (Exception ex)
      {
        Log.Error(ex, "");

        success = false;
        _error_descr = ex.Message;

        // string fname = string.Format(@"d:\LOG\{0} -- {1}", DateTime.Now.ToString("yyyy_MM_dd HH-mm-ss"), _error_descr);
      }

      finally
      {
        if (!success)
        {
          lock (_locker)
          {
            _succeeded = false;
          }
        }
      }

      return null;
    }

    private WeatherInfo get_nsu_current_temp( )
    {
      bool success = true;
      lock (_locker)
      {
        if (_extractor == null)
          return null;
      }

      try
      {
        WeatherInfo wi = new WeatherInfo();
        _extractor.get_nsu_current_temp(wi);

        return wi;
      }
      catch (Exception ex)
      {
        Log.Error(ex, "");

        success = false;
        _error_descr = ex.Message;
      }
      finally
      {
        if (!success)
        {
          lock (_locker)
          {
            _succeeded = false;
          }
        }
      }
      return null;
    }
  }
}
