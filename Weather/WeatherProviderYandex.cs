using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using System.Xml;
using System.Xml.Serialization;

namespace weather
{
  public class YandexWeatherExtractor
  {

    static WeatherPeriod[] day_periods = new WeatherPeriod[]
    {
      WeatherPeriod.TodayMorning,            WeatherPeriod.TodayDay,            WeatherPeriod.TodayEvening,            WeatherPeriod.TodayNight,            
      WeatherPeriod.TomorrowMorning,         WeatherPeriod.TomorrowDay,         WeatherPeriod.TomorrowEvening,         WeatherPeriod.TomorrowNight,         
      WeatherPeriod.DayAfterTomorrowMorning, WeatherPeriod.DayAfterTomorrowDay, WeatherPeriod.DayAfterTomorrowEvening, WeatherPeriod.DayAfterTomorrowNight
    };

    static Dictionary<string, WindDirection> wind_direction_encoding = new Dictionary<string, WindDirection>()
      { 
        { "С", WindDirection.N }, 
        { "В", WindDirection.E }, 
        { "Ю", WindDirection.S }, 
        { "З", WindDirection.W }, 
        { "СВ", WindDirection.NE }, 
        { "СЗ", WindDirection.NW }, 
        { "ЮВ", WindDirection.SE },
        { "ЮЗ", WindDirection.SW } 
    };

    static Dictionary<string, WeatherType> weather_type_encoding = new Dictionary<string, WeatherType>()
    {
      { "bkn-d",      WeatherType.Cloudy }, // - облачно с прояснениями
      { "bkn-n",      WeatherType.Cloudy }, // - облачно с прояснениями

      { "bkn-m-sn-d", WeatherType.CloudyPartlySnowy }, // - небольшой снег
      { "bkn-m-sn-n", WeatherType.CloudyPartlySnowy }, // - Небольшой снег
      { "bkn-m-ra-d", WeatherType.CloudyPartlyRainy }, // - Небольшой дождь
      { "bkn-m-ra-n", WeatherType.CloudyPartlyRainy }, // - Небольшой дождь

      { "bkn-ra-d",   WeatherType.CloudyRainy }, // - Дождь
      { "bkn-ra-n",   WeatherType.CloudyRainy }, // - Дождь
      { "bkn-sn-d",   WeatherType.CloudySnowy }, // - Снег
      { "bkn-sn-n",   WeatherType.CloudySnowy }, // - Снег

      { "bkn-p-ra-n", WeatherType.CloudyRainyStorm }, // - Ливень
      { "bkn-p-ra-d", WeatherType.CloudyRainyStorm }, // - Ливень
      { "bkn-p-sn-n", WeatherType.CloudySnowyStorm }, // - Снег
      { "bkn-p-sn-d", WeatherType.CloudySnowyStorm }, // - Снег

      { "bl",         WeatherType.Blizzard },         // — метель
      { "fg-d",       WeatherType.Fog },                    // — туман
      { "fg-n",       WeatherType.Fog },                    // — туман

      { "ovc",        WeatherType.Overcast },               // — пасмурно

      { "ovc-m-ra",   WeatherType.OvercastPartlyRainy }, // - небольшой дождь
      { "ovc-m-sn",   WeatherType.OvercastPartlySnowy }, // - небольшой снег

      { "ovc-ra",     WeatherType.OvercastRainy }, // - дождь
      { "ovc-sn",     WeatherType.OvercastSnowy }, // - Снег
      
      { "ovc-p-ra",   WeatherType.OvercastRainyStorm }, // - ливень
      { "ovc-p-sn",   WeatherType.OvercastSnowyStorm }, // - снег

      { "ovc-ra-sn",  WeatherType.OvercastSnowy }, // - Дождь со снегом
      { "ovc-ts-ra",  WeatherType.OvercastLightningRainy }, //  — облачно, дождь, гроза

      { "skc-n",      WeatherType.Clear }, // - Малооблачно
      { "skc-d",      WeatherType.Clear }, // - Ясно
    };

    private IWeatherReader _sitereader = null;

    public YandexWeatherExtractor(IWeatherReader reader)
    {
#if STATISTICS
      read_statistics();
#endif
      if (reader != null)
        _sitereader = reader;
      else
        _sitereader = new YandexFileReaderWriter(WeatherSource.NC);
    }

    public void get_current_weather(WeatherInfo w)
    {
      string current = _sitereader.current();
      if (current == null)
        throw new Exception("incorrect current weather structure ");

      XmlDocument pg = new XmlDocument();
      pg.LoadXml(current);

      XmlNode pgd_current = pg.DocumentElement;

      // weather character
      string wt = get_weather_type(pgd_current, "./div/a/img");
      w.WeatherType = weather_type_encoding.Keys.Contains(wt) ? weather_type_encoding[wt] : WeatherType.Undefined;

#if STATISTICS
      if (!stat_weather_type.Keys.Contains(wt))
      {
        class_name = "link__condition";
        XmlNode weather_type = pgd_current.SelectSingleNode($"./div/a/div/div[starts-with(@class, '{class_name}')]");
        if (weather_type == null)
          throw new Exception("incorrect current weather structure-- cant find weather type string");

        string wts = weather_type.InnerText;
        write_statistics(wt, wts);
      }
#endif
      // air temperature
      w.TemperatureHigh = w.TemperatureLow = get_air_temperature(pgd_current, "./div/a/div/span[@class = 'temp__value temp__value_with-unit']");

      // wind 
      w.WindSpeed = null;
      XmlNode wind_speed = pgd_current.SelectSingleNode($"./div[@class = 'fact__props']//span[@class = 'wind-speed']");
      if (wind_speed == null)
        throw new Exception("incorrect current weather structure -- cant find wind speed");

      if (double.TryParse(wind_speed.InnerText.Replace(',', '.').Replace('−', '-').Trim(), 
          NumberStyles.Number, new CultureInfo("en"), out double windspeed))
        w.WindSpeed = windspeed;

      w.WindDirection = WindDirection.Undefined;
      XmlNode wind_dir = pgd_current.SelectSingleNode($"./div[@class = 'fact__props']//span[@class = 'fact__unit']/abbr");
      if (wind_dir == null)
        throw new Exception("incorrect current weather structure -- cant find wind direction");

      string wind_dir_name = wind_dir.InnerText;
      if (wind_direction_encoding.Keys.Contains(wind_dir_name))
        w.WindDirection = wind_direction_encoding[wind_dir_name];

      // humidity
      w.Humidity = null;
      XmlNode humidity = pgd_current.SelectSingleNode($"./div[@class = 'fact__props']//div[@class = 'term term_orient_v fact__humidity']");
      if (humidity == null)
        throw new Exception("incorrect current weather structure -- cant find humidity");

      if (double.TryParse(humidity.InnerText.Replace(',', '.').Replace('−', '-').Replace('%', ' ').Trim(),
          NumberStyles.Number, new CultureInfo("en"), out double hum))
        w.Humidity = hum;

      // pressure 
      w.Pressure = null;
      XmlNode pressure = pgd_current.SelectSingleNode($"./div[@class = 'fact__props']//div[@class = 'term term_orient_v fact__pressure']");
      if (pressure == null)
        throw new Exception("incorrect current weather structure -- cant find pressure");

      if (double.TryParse(pressure.InnerText.Split(' ')[0].Replace(',', '.').Replace('−', '-').Trim(),
          NumberStyles.Number, new CultureInfo("en"), out double press))
        w.Pressure = press;

      // hourly temperature forecast
      var hour_spans = pgd_current.SelectNodes("//span[@class = 'fact__hour-elem']");
      foreach (XmlNode hour_span in hour_spans)
      {
        wt = get_weather_type(hour_span, "./img");
        if (!wt.Equals("sunset") && !wt.Equals("sunrise"))
        {
          ShortWeatherInfo swi = new ShortWeatherInfo();
          double ? temp = get_air_temperature(hour_span, "./div[@class = 'fact__hour-temp']");

          XmlNode hour = hour_span.SelectSingleNode($"./div[@class = 'fact__hour-label']");
          if (hour != null && temp.HasValue)
          {
            if (int.TryParse(hour.InnerText.Trim().Split(':')[0],
                NumberStyles.Number, new CultureInfo("en"), out int hr))
            {
              w.HourlyWeather.Add(new ShortWeatherInfo() 
              { 
                Hour = hr,
                Temperature =  temp.Value, 
                WeatherType = weather_type_encoding.Keys.Contains(wt) ? weather_type_encoding[wt] : WeatherType.Undefined
              });
            }
          }
        }

#if STATISTICS
        if (!wt.Equals("sunset") && !wt.Equals("sunrise") && !stat_weather_type.Keys.Contains(wt))
        {
          string wts = hour_span.SelectSingleNode("@aria-label").Value;
          wts = wts.Substring(0, wts.IndexOf(','));
          for (int i = 0; i < 3; i++)
            wts = wts.Substring(wts.IndexOf(' ') + 1);

          write_statistics(wt, wts);
        }
#endif

      }
    }

    public void get_nsu_current_temp(WeatherInfo w)
    {
      string st = _sitereader.temperature();
      if (st != null || !st.Contains("°"))
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
      if (forecast == null)
        throw new Exception("incorrect current weather structure ");

      XmlDocument pg = new XmlDocument();
      pg.LoadXml(forecast);

      XmlNode pgd_forecast = pg.DocumentElement;
      var day_divs = pgd_forecast.SelectNodes("//div[@class = 'card']");
      int day_period = 0;
      for (int day = 0; day < day_divs.Count; day++)
      {
        XmlNode day_div = day_divs[day];

        var table_rows = day_div.SelectNodes("./dd[@class='forecast-details__day-info']/table[@class = 'weather-table']/tbody[@class = 'weather-table__body']/tr[@class = 'weather-table__row']");
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
#if STATISTICS
      if (!wt.Equals("sunset") && !wt.Equals("sunrise") && !stat_weather_type.Keys.Contains(wt))
      {
        string wts = row.SelectSingleNode("./td[@class = 'weather-table__body-cell weather-table__body-cell_type_condition']").InnerText;
        write_statistics(wt, wts);
      }
#endif

      // temperature
      var air_temps = row.SelectNodes("./td/div/div//span[@class='temp__value temp__value_with-unit']");
      if (air_temps.Count == 2)
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
      XmlNode wind_speed = row.SelectSingleNode("./td/div/span/span[@class = 'wind-speed']");
      if (wind_speed == null)
        throw new Exception("incorrect forecast structure -- cant find wind speed");

      if (double.TryParse(wind_speed.InnerText.Replace(',', '.').Replace('−', '-').Trim(),
          NumberStyles.Number, new CultureInfo("en"), out double windspeed))
        w.WindSpeed = windspeed;

      w.WindDirection = WindDirection.Undefined;
      XmlNode wind_dir = row.SelectSingleNode($"./td/div/div[@class = 'weather-table__wind-direction']/abbr");
      if (wind_dir == null)
        throw new Exception("incorrect current weather structure -- cant find wind direction");

      string wind_dir_name = wind_dir.InnerText;
      if (wind_direction_encoding.Keys.Contains(wind_dir_name))
        w.WindDirection = wind_direction_encoding[wind_dir_name];
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

    private static double ? get_air_temperature(XmlNode pgd_node, string node_selector = null)
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


#if STATISTICS
    static string stat_file_name = "yandex_weather_types.xml";
    public static Dictionary<string, string> stat_weather_type = new Dictionary<string, string>();
    public class WeatherTypeDescription
    {
      [XmlAttribute]
      public string WeatherType;
      [XmlAttribute]
      public string WeatherDescription;
    }

    private static void read_statistics( )
    {
      XmlSerializer serializer = new XmlSerializer(typeof(WeatherTypeDescription[]), new XmlRootAttribute() { ElementName = "WeatherTypes" });

      if (File.Exists(stat_file_name))
      {
        FileStream readstream = new FileStream(stat_file_name, FileMode.Open);
        YandexWeatherExtractor.stat_weather_type = ((WeatherTypeDescription[])serializer.Deserialize(readstream)).ToDictionary(i => i.WeatherType, i => i.WeatherDescription);

        readstream.Close();
      }
    }

    private static void write_statistics(string wt, string wtd)
    {
      stat_weather_type[wt] = wtd;
      XmlSerializer serializer = new XmlSerializer(typeof(WeatherTypeDescription[]), new XmlRootAttribute() { ElementName = "WeatherTypes" });

      FileStream writestream = new FileStream(stat_file_name, FileMode.OpenOrCreate);
      serializer.Serialize(writestream, stat_weather_type.Select(kv => new WeatherTypeDescription() { WeatherType = kv.Key, WeatherDescription = kv.Value }).ToArray());

      writestream.Close();

    }
#endif

  }

  public class WeatherProviderYandex : WeatherProviderBase
  {
    private static YandexWeatherExtractor _extractor;
    private static IWeatherProvider _self = null;
    private static int _refcounter = 0;

    private WeatherProviderYandex(IWeatherReader reader)
    {
      _extractor = new YandexWeatherExtractor(reader);
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
      WeatherInfo w = new WeatherInfo();

      get_nsu_current_temp(w);
      get_current_weather(w);

      lock (_locker)
      {
        _weather.Clear();
        _weather[WeatherPeriod.Now] = w;
      }

    }

    protected override void read_forecast( )
    {
      Dictionary<WeatherPeriod, WeatherInfo> weather = new Dictionary<WeatherPeriod, WeatherInfo>();

      get_forecast(weather);

      lock (_locker)
      {
        foreach (WeatherPeriod w in weather.Keys)
          _weather[w] = weather[w];

      }
    }

    private void get_current_weather(WeatherInfo w)
    {
      bool success = true;
      _succeeded = true;

      try
      {
        _extractor.get_current_weather(w);
      }
      catch (Exception e)
      {
        success = false;
        _error_descr = e.Message;

        string fname = string.Format(@"d:\LOG\{0} -- {1}", DateTime.Now.ToString("yyyy_MM_dd HH-mm-ss"), _error_descr);
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
    }

    private void get_forecast(Dictionary<WeatherPeriod, WeatherInfo> weather)
    {
      bool success = true;
      _succeeded = true;

      try
      {
        _extractor.get_forecast(weather);
      }
      catch (Exception e)
      {
        success = false;
        _error_descr = e.Message;

        string fname = string.Format(@"d:\LOG\{0} -- {1}", DateTime.Now.ToString("yyyy_MM_dd HH-mm-ss"), _error_descr);
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
    }

    private void get_nsu_current_temp(WeatherInfo w)
    {
      bool success = true;
      try
      {
        _extractor.get_nsu_current_temp(w);
      }
      catch (Exception e)
      {
        success = false;
        _error_descr = e.Message;
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
    }
  }
}
