using System;
using System.Collections.Generic;
using System.Linq;

using System.Threading;
using System.Xml;

namespace weather
{
  // http://www.wunderground.com/

  class WeatherProviderWU : WeatherProviderBase
  {
    static string _wu_key = "22051dd96a426176";

    static Dictionary<string, WindDirection> wind_direction_encoding = new Dictionary<string, WindDirection>()
    {
      { "N",   WindDirection.N }, { "NNE", WindDirection.NNE }, { "NE",  WindDirection.NE }, { "ENE", WindDirection.ENE },
      { "E",   WindDirection.E }, { "ESE", WindDirection.ESE }, { "SE",  WindDirection.SE }, { "SSE", WindDirection.SSE },
      { "S",   WindDirection.S }, { "SSW", WindDirection.SSW }, { "SW",  WindDirection.SW }, { "WSW", WindDirection.WSW },
      { "W",   WindDirection.W }, { "WNW", WindDirection.WNW }, { "NW",  WindDirection.NW }, { "NNW", WindDirection.NNW }
    };

    static Dictionary<double, WindDirection> wind_direction_degrees = new Dictionary<double, WindDirection>()
    {
      { 0.0      , WindDirection.N }, { 22.5 * 1 , WindDirection.NNE }, { 22.5 * 2 , WindDirection.NE }, { 22.5 * 3 , WindDirection.ENE },
      { 22.5 * 4 , WindDirection.E }, { 22.5 * 5 , WindDirection.ESE }, { 22.5 * 6 , WindDirection.SE }, { 22.5 * 7 , WindDirection.SSE },
      { 22.5 * 8 , WindDirection.S }, { 22.5 * 9 , WindDirection.SSW }, { 22.5 * 10, WindDirection.SW }, { 22.5 * 11, WindDirection.WSW },
      { 22.5 * 12, WindDirection.W }, { 22.5 * 13, WindDirection.WNW }, { 22.5 * 14, WindDirection.NW }, { 22.5 * 15, WindDirection.NNW },
      { 22.5 * 16, WindDirection.N }
    };

    static Dictionary<string, WeatherType> weather_type_encoding = new Dictionary<string, WeatherType>()
    {
      { "chanceflurries", WeatherType.Cloudy },
      { "chancerain",     WeatherType.CloudyPartlyRainy },
      { "chancesleet",    WeatherType.CloudyPartlySnowy },
      { "chancesnow",     WeatherType.CloudyPartlySnowy },
      { "chancetstorms",  WeatherType.OvercastLightningRainy },
      { "clear",          WeatherType.Clear },
      { "cloudy",         WeatherType.Overcast },
      { "flurries",       WeatherType.Overcast },
      { "fog",            WeatherType.Fog },
      { "hazy",           WeatherType.Fog },
      { "mostlycloudy",   WeatherType.Cloudy },
      { "mostlysunny",    WeatherType.PartlyCloudy },
      { "partlycloudy",   WeatherType.Cloudy },
      { "partlysunny",    WeatherType.PartlyCloudy },
      { "sleet",          WeatherType.CloudySnowy },
      { "rain",           WeatherType.OvercastRainy },
      { "snow",           WeatherType.OvercastSnowy },
      { "sunny",          WeatherType.Clear },
      { "tstorms",        WeatherType.OvercastLightningRainy }
    };

    private static IWeatherProvider _self = null;
    private static int _refcounter = 0;

    private WeatherProviderWU()
    {
    }

    public static IWeatherProvider get()
    {
      if (_self == null)
        _self = new WeatherProviderWU();

      _refcounter++;
      return _self;
    }

    public override int release()
    {
      if (--_refcounter == 0)
        close();

      return _refcounter;
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

          //current conditions 
          forecast.Load("http://api.wunderground.com/api/" + _wu_key + "/conditions/q/Russia/Novosibirsk.xml");

          XmlElement root = forecast.DocumentElement;
          XmlNode now = root.SelectSingleNode("/response/current_observation");
          extract_current_weather(now);

          forecast.Load("http://api.wunderground.com/api/" + _wu_key + "/hourly10day/q/Russia/Novosibirsk.xml");
          root = forecast.DocumentElement;

          DateTime today = DateTime.Now;
          for (int i = 0; i < 2; i++)
          {
            DateTime day = today.Add(TimeSpan.FromDays(i));

            //// НЕ ДОДЕЛАНО!
            string select = string.Format("//forecast[(./FCTTIME/mday='{0}') and (./FCTTIME/mon='{1}') and (./FCTTIME/year='{2}')]", day.Day, day.Month, day.Year);
            XmlNodeList nodes = root.SelectNodes(select);
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

    private void extract_current_weather(XmlNode node)
    {
      WeatherInfo w = new WeatherInfo
      {
        TemperatureLow = double.Parse(node["temp_c"].InnerText),
        TemperatureHigh = double.Parse(node["temp_c"].InnerText),
        Pressure = double.Parse(node["pressure_mb"].InnerText) * 0.75006375541921,
        WindSpeed = double.Parse(node["wind_kph"].InnerText) / 3.6,
        WeatherType = weather_type_encoding.Keys.Contains(node["icon"].InnerText) ? weather_type_encoding[node["icon"].InnerText] : WeatherType.Undefined
      };

      string hum = node["relative_humidity"].InnerText;
      hum = hum.Substring(0, hum.IndexOf('%'));
      w.Humidity = double.Parse(hum);

      // WindDirection = wind_direction_encoding.Keys.Contains(node["wind_direction"].InnerText) ? wind_direction_encoding[node["wind_direction"].InnerText] : WindDirection.Undefined,
      double wind_degrees = double.Parse(node["wind_degrees"].InnerText);
      foreach (var d in wind_direction_degrees.Keys)
        if (wind_degrees >= d - 22.5 / 2.0 && wind_degrees < d + 22.5 / 2.0)
        {
          w.WindDirection = wind_direction_degrees[d];
          break;
        }
    }

    private void extract_weather(int day_from_today, XmlNode node)
    {
      switch (node.Attributes["type"].Value)
      {
        case "morning":
          extract_weather_forecast(day_from_today == 0 ? WeatherPeriod.TodayMorning : (day_from_today == 1 ? WeatherPeriod.TomorrowMorning : (day_from_today == 2 ? WeatherPeriod.DayAfterTomorrowMorning : WeatherPeriod.Undefined)), node);
          break;

        case "day":
          extract_weather_forecast(day_from_today == 0 ? WeatherPeriod.TodayDay : (day_from_today == 1 ? WeatherPeriod.TomorrowDay : (day_from_today == 2 ? WeatherPeriod.DayAfterTomorrowDay : WeatherPeriod.Undefined)), node);
          break;

        case "evening":
          extract_weather_forecast(day_from_today == 0 ? WeatherPeriod.TodayEvening : (day_from_today == 1 ? WeatherPeriod.TomorrowEvening : (day_from_today == 2 ? WeatherPeriod.DayAfterTomorrowEvening : WeatherPeriod.Undefined)), node);
          break;

        case "night":
          extract_weather_forecast(day_from_today == 0 ? WeatherPeriod.TodayNight : (day_from_today == 1 ? WeatherPeriod.TomorrowNight : (day_from_today == 2 ? WeatherPeriod.DayAfterTomorrowNight : WeatherPeriod.Undefined)), node);
          break;
      }
    }

    private void extract_weather_forecast(WeatherPeriod weatherPeriod, XmlNode node)
    {

    }
  }
}
