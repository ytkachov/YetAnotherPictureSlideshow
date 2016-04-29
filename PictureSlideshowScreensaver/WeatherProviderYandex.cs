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

    public WeatherProviderYandex()
    {
    }

    protected override void readdata()
    {
      while (true)
      {
        try
        {
          lock (_locker)
          {
            _weather.Clear();
          }

          XmlDocument forecast = new XmlDocument();

          //список городов здесь: http://weather.yandex.ru/static/cities.xml
          // для новосибирска ID = 29634
          forecast.Load("https://export.yandex.ru/weather-ng/forecasts/29634.xml");
          //forecast.Load("file://d:/1.xml");

          if (_nsmgr == null)
          {
            _nsmgr = new XmlNamespaceManager(forecast.NameTable);
            _nsmgr.AddNamespace("Y", "http://weather.yandex.ru/forecast");
          }

          XmlElement root = forecast.DocumentElement;
          XmlNode now = root.SelectSingleNode("/Y:forecast/Y:fact", _nsmgr);
          extract_weather(WeatherPeriod.Now, now);

          DateTime today = DateTime.Now;
          for (int i = 0; i < 2; i++)
          {
            DateTime day = today.Add(TimeSpan.FromDays(i));

            XmlNodeList nodes = root.SelectNodes(string.Format("//Y:day[@date='{0}-{1:D2}-{2:D2}']/Y:day_part", day.Year, day.Month, day.Day), _nsmgr);
            foreach (XmlNode node in nodes)
              extract_weather(i, node);
          }
        }
        catch (Exception ex)
        {
          ex.ToString();
        }

        if (_exit.WaitOne(TimeSpan.FromMinutes(30)))
        {
          break;
        }
      }

    }

    private void extract_weather(WeatherPeriod period, XmlNode node)
    {
      if (period == WeatherPeriod.Undefined)
        return;

      weather w = new weather
      {
        Pressure = double.Parse(node["pressure"].InnerText),
        Humidity = double.Parse(node["humidity"].InnerText),
        WindSpeed = double.Parse(node["wind_speed"].InnerText),
        WindDirection = wind_direction_encoding.Keys.Contains(node["wind_direction"].InnerText) ? wind_direction_encoding[node["wind_direction"].InnerText] : WindDirection.Undefined,
        WeatherType = weather_type_encoding.Keys.Contains(node["image-v3"].InnerText) ? weather_type_encoding[node["image-v3"].InnerText] : WeatherType.Undefined
      };

      XmlNode temp = node.SelectSingleNode("Y:temperature", _nsmgr);
      if (temp != null)
      {
        w.TemperatureLow = w.TemperatureHigh = double.Parse(temp.InnerText);
      }
      else
      {
        w.TemperatureLow = w.TemperatureHigh = double.Parse(node.SelectSingleNode("descendant::Y:from", _nsmgr).InnerText);
        w.TemperatureHigh = w.TemperatureHigh = double.Parse(node.SelectSingleNode("descendant::Y:to", _nsmgr).InnerText);
      }

      lock (_locker)
      {
        _weather[period] = w;
      }
    }

    private void extract_weather(int day_from_today, XmlNode node)
    {
      switch(node.Attributes["type"].Value)
      {
        case "morning":
          extract_weather(day_from_today == 0 ? WeatherPeriod.TodayMorning : (day_from_today == 1 ? WeatherPeriod.TomorrowMorning : (day_from_today == 2 ? WeatherPeriod.DayAfterTomorrowMorning : WeatherPeriod.Undefined)), node);
          break;

        case "day":
          extract_weather(day_from_today == 0 ? WeatherPeriod.TodayDay : (day_from_today == 1 ? WeatherPeriod.TomorrowDay : (day_from_today == 2 ? WeatherPeriod.DayAfterTomorrowMorning : WeatherPeriod.Undefined)), node);
          break;

        case "evening":
          extract_weather(day_from_today == 0 ? WeatherPeriod.TodayEvening : (day_from_today == 1 ? WeatherPeriod.TomorrowEvening : (day_from_today == 2 ? WeatherPeriod.DayAfterTomorrowMorning : WeatherPeriod.Undefined)), node);
          break;

        case "night":
          extract_weather(day_from_today == 0 ? WeatherPeriod.TodayNight : (day_from_today == 1 ? WeatherPeriod.TomorrowNight : (day_from_today == 2 ? WeatherPeriod.DayAfterTomorrowMorning : WeatherPeriod.Undefined)), node);
          break;
      }
    }
  }
}
