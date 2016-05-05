using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Threading;

using WatiN.Core;

namespace weather
{
  class WeatherProviderNSU : WeatherProviderBase
  {
    private static IWeatherProvider _self = null;
    private static int _refcounter = 0;

    private bool _succeeded = false;

    static Dictionary<string, WindDirection> wind_direction_encoding = new Dictionary<string, WindDirection>()
    {
      { "north",   WindDirection.N }, { "north_east",  WindDirection.NE },
      { "east", WindDirection.E }, { "south_east",   WindDirection.SE },
      { "south", WindDirection.S }, { "south_west",   WindDirection.SW },
      { "west", WindDirection.W }, { "north_west",   WindDirection.NW }
    };

    static Dictionary<string, WeatherType> weather_type_encoding = new Dictionary<string, WeatherType>()
    {
      { "sunshine_light_rain_day",            WeatherType.CloudyPartlyRainy },
      { "sunshine_light_snow_day",            WeatherType.CloudyPartlyRainy },
      { "sunshine_rain_day",                  WeatherType.CloudyPartlyRainy },
      { "sunshine_none_day",                  WeatherType.CloudyPartlyRainy },
      { "partly_cloudy_rain_day",             WeatherType.CloudyPartlyRainy },
      { "partly_cloudy_light_rain_day",       WeatherType.CloudyPartlyRainy },
      { "partly_cloudy_snow_day",             WeatherType.CloudyPartlyRainy },
      { "partly_cloudy_light_snow_day",       WeatherType.CloudyPartlyRainy },
      { "partly_cloudy_thunderstorm_day",     WeatherType.CloudyPartlyRainy },
      { "partly_cloudy_none_day",             WeatherType.CloudyPartlyRainy },
      { "mostly_cloudy_rain_day",             WeatherType.CloudyPartlyRainy },
      { "mostly_cloudy_light_rain_day",       WeatherType.CloudyPartlyRainy },
      { "mostly_cloudy_snow_day",             WeatherType.CloudyPartlyRainy },
      { "mostly_cloudy_light_snow_day",       WeatherType.CloudyPartlyRainy },
      { "mostly_cloudy_thunderstorm_day",     WeatherType.CloudyPartlyRainy },
      { "mostly_cloudy_none_day",             WeatherType.CloudyPartlyRainy },
      { "cloudy_rain_day",                    WeatherType.CloudyPartlyRainy },
      { "cloudy_light_rain_day",              WeatherType.CloudyPartlyRainy },
      { "cloudy_snow_day",                    WeatherType.CloudyPartlyRainy },
      { "cloudy_light_snow_day",              WeatherType.CloudyPartlyRainy },
      { "cloudy_thunderstorm_day",            WeatherType.CloudyPartlyRainy },
      { "cloudy_none_day",                    WeatherType.CloudyPartlyRainy },
      { "sunshine_light_rain_night",          WeatherType.CloudyPartlyRainy },
      { "sunshine_light_snow_night",          WeatherType.CloudyPartlyRainy },
      { "sunshine_none_night",                WeatherType.CloudyPartlyRainy },
      { "partly_cloudy_rain_night",           WeatherType.CloudyPartlyRainy },
      { "partly_cloudy_light_rain_night",     WeatherType.CloudyPartlyRainy },
      { "partly_cloudy_snow_night",           WeatherType.CloudyPartlyRainy },
      { "partly_cloudy_light_snow_night",     WeatherType.CloudyPartlyRainy },
      { "partly_cloudy_thunderstorm_night",   WeatherType.CloudyPartlyRainy },
      { "partly_cloudy_none_night",           WeatherType.CloudyPartlyRainy },
      { "mostly_cloudy_rain_night",           WeatherType.CloudyPartlyRainy },
      { "mostly_cloudy_light_rain_night",     WeatherType.CloudyPartlyRainy },
      { "mostly_cloudy_snow_night",           WeatherType.CloudyPartlyRainy },
      { "mostly_cloudy_light_snow_night",     WeatherType.CloudyPartlyRainy },
      { "mostly_cloudy_thunderstorm_night",   WeatherType.CloudyPartlyRainy },
      { "mostly_cloudy_none_night",           WeatherType.CloudyPartlyRainy },
      { "cloudy_rain_night",                  WeatherType.CloudyPartlyRainy },
      { "cloudy_light_rain_night",            WeatherType.CloudyPartlyRainy },
      { "cloudy_snow_night",                  WeatherType.CloudyPartlyRainy },
      { "cloudy_light_snow_night",            WeatherType.CloudyPartlyRainy },
      { "cloudy_thunderstorm_night",          WeatherType.CloudyPartlyRainy },
      { "cloudy_none_night",                  WeatherType.CloudyPartlyRainy },
      { "sunshine_rain_night",                WeatherType.CloudyPartlyRainy }
    };
    private IE browser_;

    private WeatherProviderNSU()
    {
    }

    public static IWeatherProvider get()
    {
      if (_self == null)
        _self = new WeatherProviderNSU();

      _refcounter++;
      return _self;
    }

    public override int release()
    {
      if (--_refcounter == 0)
        close();

      return _refcounter;
    }

    protected override void close()
    {
      base.close();

      if (browser_ != null)
        browser_.Close();

      browser_ = null;
    }

    protected override void readdata()
    {
      Settings.AutoMoveMousePointerToTopLeft = false;
      Settings.MakeNewIeInstanceVisible = false;
      browser_ = new IE();

      while (true)
      {
        lock (_locker)
        {
          _weather.Clear();
        }

        weather w = new weather();
        read_ngs_current_weather(w);
        if (!_succeeded)
          read_nsu_current_temp(w);

        if (_exit.WaitOne(TimeSpan.FromMinutes(10)))
          break;
      }
    }

    private void read_nsu_current_temp(weather w)
    {
      bool success = false;
      try
      {
        browser_.GoTo("http://weather.nsu.ru/");
        Span temp = browser_.Span(Find.ById("temp"));

        if (temp.Exists)
        {
          Thread.Sleep(500);

          string st = temp.Text;
          if (st != null || !st.Contains("°"))
          {
            success = true;
            int d = st.IndexOf("°");
            double t = double.Parse(st.Substring(0, d));
            lock (_locker)
            {
              w.TemperatureLow = w.TemperatureHigh = t;
              _succeeded = true;
            }
          }
        }
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

    private void read_ngs_current_weather(weather w)
    {
      bool success = true;
      _succeeded = true;

      try
      {
        browser_.GoTo("http://pogoda.ngs.ru/academgorodok");

        Div curr = browser_.Div(Find.ByClass("today-panel__info__main__item first"));
        if (!curr.Exists)
        {
          _error_descr = "incorrect structure 0";
          success = false;
        }
        else
        {
          // temperature
          Span temp = browser_.Span(Find.ByClass("value__main"));
          if (!curr.Exists)
            success = false;
          else
          {
            string st = temp.Text;
            if (string.IsNullOrEmpty(st))
            {
              _error_descr = "incorrect structure 1";
              success = false;
            }
            else
            {
              st = st.Replace(',', '.');
              double t = double.Parse(st);
              lock (_locker)
              {
                w.TemperatureHigh = w.TemperatureLow = t;
              }
            }
          }

          ElementCollection dls = curr.ElementsWithTag("dl");
          if (dls.Count != 3)
          {
            _error_descr = "incorrect structure 2";
            success = false;
          }
          {
            // wind
            ElementCollection elements = ((IElementContainer)dls[0]).Elements;
            foreach (Element e in elements)
            {
              string class_name = "icon-small icon-wind-";
              if (e.TagName.Equals("dt", StringComparison.InvariantCultureIgnoreCase))
              {
                string wind = e.Text.TrimStart(' ');
                double ws;
                if (!double.TryParse(wind.Substring(0, wind.IndexOf(' ')), out ws))
                  ws = 0.0;

                w.WindSpeed = ws;
              }
              else if (e.TagName.Equals("i", StringComparison.InvariantCultureIgnoreCase) && e.ClassName.StartsWith(class_name))
              {
                string wd = e.ClassName.Substring(class_name.Length);
                w.WindDirection = wind_direction_encoding.Keys.Contains(wd) ? wind_direction_encoding[wd] : WindDirection.Undefined;
              }
            }

            // pressure
            elements = ((IElementContainer)dls[1]).Elements;
            foreach (Element e in elements)
            {
              if (e.TagName.Equals("dt", StringComparison.InvariantCultureIgnoreCase))
              {
                double p;
                string wind = e.Text.TrimStart(' ');
                if (!double.TryParse(wind.Substring(0, wind.IndexOf(' ')), out p))
                  p = 0.0;

                w.Pressure = p;
              }
            }

            // humidity
            elements = ((IElementContainer)dls[2]).Elements;
            foreach (Element e in elements)
            {
              if (e.TagName.Equals("dt", StringComparison.InvariantCultureIgnoreCase))
              {
                double h;
                string wind = e.Text.TrimStart(' ');
                if (!double.TryParse(wind.Substring(0, wind.IndexOf('%', ' ')), out h))
                  h = 0.0;

                w.Humidity = h;
              }
            }
          }

        }

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
