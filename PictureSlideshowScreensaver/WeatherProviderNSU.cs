using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Threading;

using WatiN.Core;

namespace weather
{
  public static class WatinExtensions
  {
    //public static ElementCollection Children(this Element self)
    //{
    //  return self.DomContainer.Elements.Filter(e => self.Equals(e.Parent));
    //}
    public static DivCollection ChildDivs(this Element self)
    {
      return self.DomContainer.Divs.Filter(e => self.Equals(e.Parent));
    }
    public static ElementCollection EChildDivs(this Element self)
    {
      return self.DomContainer.Elements.Filter(e => (self.Equals(e.Parent) && e.TagName.ToLower() == "div"));
    }
  }

  class WeatherProviderNSU : WeatherProviderBase
  {
    private static IWeatherProvider _self = null;
    private static int _refcounter = 0;

    private bool _succeeded = false;
    private class StringStartsWith : WatiN.Core.Comparers.Comparer<string>
    {
      public StringStartsWith()
      {
      }

      public string startswith { get; set; }

      public override bool Compare(string V)
      {
        return V != null && V.StartsWith(startswith);
      }
    }

    static Dictionary<string, WeatherPeriod>[] _day_periods = new Dictionary<string, WeatherPeriod>[3]
    {
      new Dictionary<string, WeatherPeriod>() { { "ночь", WeatherPeriod.TodayNight },            { "утро", WeatherPeriod.TodayMorning },            { "день", WeatherPeriod.TodayDay },            { "вечер", WeatherPeriod.TodayEvening } },
      new Dictionary<string, WeatherPeriod>() { { "ночь", WeatherPeriod.TomorrowNight },         { "утро", WeatherPeriod.TomorrowMorning },         { "день", WeatherPeriod.TomorrowDay },         { "вечер", WeatherPeriod.TomorrowEvening } },
      new Dictionary<string, WeatherPeriod>() { { "ночь", WeatherPeriod.DayAfterTomorrowNight }, { "утро", WeatherPeriod.DayAfterTomorrowMorning }, { "день", WeatherPeriod.DayAfterTomorrowDay }, { "вечер", WeatherPeriod.DayAfterTomorrowEvening } }
    };

    static Dictionary<string, WindDirection> wind_direction_encoding = new Dictionary<string, WindDirection>()
    {
      { "north",   WindDirection.N }, { "north_east",  WindDirection.NE },
      { "east", WindDirection.E }, { "south_east",   WindDirection.SE },
      { "south", WindDirection.S }, { "south_west",   WindDirection.SW },
      { "west", WindDirection.W }, { "north_west",   WindDirection.NW }
    };

    static Dictionary<string, WeatherType> weather_type_encoding = new Dictionary<string, WeatherType>()
    {
      { "sunshine_light_rain_day",            WeatherType.ClearPartlyRainy },
      { "sunshine_light_snow_day",            WeatherType.ClearPartlySnowy },
      { "sunshine_rain_day",                  WeatherType.ClearRainy },
      { "sunshine_none_day",                  WeatherType.Clear },
      { "partly_cloudy_rain_day",             WeatherType.CloudyRainy },
      { "partly_cloudy_light_rain_day",       WeatherType.CloudyPartlyRainy },
      { "partly_cloudy_snow_day",             WeatherType.CloudySnowy },
      { "partly_cloudy_light_snow_day",       WeatherType.CloudyPartlySnowy },
      { "partly_cloudy_thunderstorm_day",     WeatherType.CloudyLightningRainy },
      { "partly_cloudy_none_day",             WeatherType.PartlyCloudy},
      { "mostly_cloudy_rain_day",             WeatherType.CloudyRainy },
      { "mostly_cloudy_light_rain_day",       WeatherType.CloudyPartlyRainy },
      { "mostly_cloudy_snow_day",             WeatherType.CloudySnowy },
      { "mostly_cloudy_light_snow_day",       WeatherType.CloudyPartlySnowy },
      { "mostly_cloudy_thunderstorm_day",     WeatherType.CloudyLightningRainy },
      { "mostly_cloudy_none_day",             WeatherType.Cloudy },
      { "cloudy_rain_day",                    WeatherType.OvercastRainy },
      { "cloudy_light_rain_day",              WeatherType.OvercastPartlyRainy },
      { "cloudy_snow_day",                    WeatherType.OvercastSnowy },
      { "cloudy_light_snow_day",              WeatherType.OvercastPartlySnowy },
      { "cloudy_thunderstorm_day",            WeatherType.OvercastLightningRainy },
      { "cloudy_none_day",                    WeatherType.Overcast },
      { "sunshine_light_rain_night",          WeatherType.ClearPartlyRainy },
      { "sunshine_light_snow_night",          WeatherType.ClearPartlySnowy },
      { "sunshine_rain_night",                WeatherType.ClearRainy },
      { "sunshine_none_night",                WeatherType.Clear },
      { "partly_cloudy_rain_night",           WeatherType.CloudyRainy },
      { "partly_cloudy_light_rain_night",     WeatherType.CloudyPartlyRainy },
      { "partly_cloudy_snow_night",           WeatherType.CloudySnowy },
      { "partly_cloudy_light_snow_night",     WeatherType.CloudyPartlySnowy },
      { "partly_cloudy_thunderstorm_night",   WeatherType.CloudyLightningRainy },
      { "partly_cloudy_none_night",           WeatherType.PartlyCloudy},
      { "mostly_cloudy_rain_night",           WeatherType.CloudyRainy },
      { "mostly_cloudy_light_rain_night",     WeatherType.CloudyPartlyRainy },
      { "mostly_cloudy_snow_night",           WeatherType.CloudySnowy },
      { "mostly_cloudy_light_snow_night",     WeatherType.CloudyPartlySnowy },
      { "mostly_cloudy_thunderstorm_night",   WeatherType.CloudyLightningRainy },
      { "mostly_cloudy_none_night",           WeatherType.Cloudy },
      { "cloudy_rain_night",                  WeatherType.OvercastRainy },
      { "cloudy_light_rain_night",            WeatherType.OvercastPartlyRainy },
      { "cloudy_snow_night",                  WeatherType.OvercastSnowy },
      { "cloudy_light_snow_night",            WeatherType.OvercastPartlySnowy },
      { "cloudy_thunderstorm_night",          WeatherType.OvercastLightningRainy },
      { "cloudy_none_night",                  WeatherType.Overcast }
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

      while (true)
      {
        try
        {
        if (browser_ == null)
          browser_ = new IE();
        }
        catch (Exception e)
        {
          _error_descr = e.Message;
          continue;
        }

        lock (_locker)
        {
          _weather.Clear();
        }

        weather w = new weather();
        read_nsu_current_temp(w);

        try
        {
          browser_.GoToNoWait("http://pogoda.ngs.ru/academgorodok/" );
          Thread.Sleep(10000);

          read_ngs_current_weather(w);
          _weather[WeatherPeriod.Now] = w;
          read_ngs_forecast();
        }
        catch (Exception e)
        {
        }

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
            double t = double.Parse(st.Substring(0, st.IndexOf("°")));
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

    private void read_ngs_forecast()
    {
      bool success = true;
      _succeeded = true;

      try
      {
        // browser_.GoTo("http://pogoda.ngs.ru/academgorodok/");
        Table pgd_detailed = browser_.Table(Find.ByClass(new StringStartsWith() { startswith = "pgd-detailed-cards elements" }));
        if (pgd_detailed == null || !pgd_detailed.Exists || pgd_detailed.GetAttributeValue("data-weather-cards-count") != "3forecast")
        {
          _error_descr = "incorrect structure";
          success = false;
        }

        extract_3days_forecast(pgd_detailed);
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

    private bool extract_3days_forecast(Table short_forecast)
    {
      TableRowCollection days = short_forecast.TableRows;
      if (days.Count != 3)
      {
        _error_descr = "incorrect days number in forecast";
        return false;
      }

      for (int day = 0; day < 3; day++)
      {
        TableRow tr = days[day];
        if (!extract_day_forecast(day, tr))
          return false;
      }

      return true;
    }

    private bool extract_day_forecast(int day, TableRow tr)
    {
      TableCell tc = tr.TableCell(Find.ByClass("elements__section-day"));
      if (!tc.Exists)
      {
        _error_descr = "cant find day " + day.ToString();
        return false;
      }

      // check day of month
      SpanCollection spans = tc.Spans;
      if (spans.Count != 1)
      {
        _error_descr = "incorrect days in day" + day.ToString();
        return false;
      }

      string dt = spans[0].Text;
      int di;
      if (!int.TryParse(dt.Substring(0, dt.IndexOf(' ')), out di))
        di = 0;

      if ((DateTime.Now + TimeSpan.FromDays(day)).Day != di)
      {
        _error_descr = "incorrect day";
        return false;
      }

      tc = tr.TableCell(Find.ByClass("elements__section-daytime"));
      if (!tc.Exists)
      {
        _error_descr = "cant find date-time in day " + day.ToString();
        return false;
      }

      ElementCollection<Div> period_divs = tc.ChildrenOfType<Div>();

      // temperature
      tc = tr.TableCell(Find.ByClass("elements__section-temperature"));
      if (!tc.Exists)
      {
        _error_descr = "cant find temperature in day " + day.ToString();
        return false;
      }

      ElementCollection<Div> temperature_divs = tc.ChildrenOfType<Div>();

      if (temperature_divs.Count != period_divs.Count)
      {
        _error_descr = "incorrect temperature count in day " + day.ToString();
        return false;
      }

      // weather type
      tc = tr.TableCell(Find.ByClass("elements__section-weather"));
      if (!tc.Exists)
      {
        _error_descr = "cant find weather type in day " + day.ToString();
        return false;
      }

      ElementCollection<Div> weather_divs = tc.ChildrenOfType<Div>();
      if (weather_divs.Count != period_divs.Count)
      {
        _error_descr = "incorrect weather type count in day " + day.ToString();
        return false;
      }

      // wind
      tc = tr.TableCell(Find.ByClass("elements__section-wind"));
      if (!tc.Exists)
      {
        _error_descr = "cant find wind in day " + day.ToString();
        return false;
      }

      ElementCollection<Div> wind_divs = tc.ChildrenOfType<Div>();
      if (wind_divs.Count != period_divs.Count)
      {
        _error_descr = "incorrect wind count in day " + day.ToString();
        return false;
      }

      // pressure
      tc = tr.TableCell(Find.ByClass("elements__section-pressure"));
      if (!tc.Exists)
      {
        _error_descr = "cant find pressure in day " + day.ToString();
        return false;
      }

      ElementCollection<Div> pressure_divs = tc.ChildrenOfType<Div>();
      if (pressure_divs.Count != period_divs.Count)
      {
        _error_descr = "incorrect pressure count in day " + day.ToString();
        return false;
      }

      // humidity
      tc = tr.TableCell(Find.ByClass("elements__section-humidity"));
      if (!tc.Exists)
      {
        _error_descr = "cant find humidity in day " + day.ToString();
        return false;
      }

      ElementCollection<Div> humidity_divs = tc.ChildrenOfType<Div>();
      if (humidity_divs.Count != period_divs.Count)
      {
        _error_descr = "incorrect humidity count in day " + day.ToString();
        return false;
      }

      int pcount = period_divs.Count;
      for (int period = 0; period < pcount; period++)
      {
        weather w = new weather();

        // weather period
        string pname = period_divs[period].Text;
        if (!_day_periods[day].Keys.Contains(pname))
        {
          _error_descr = "invalid period name";
          return false;
        }
        WeatherPeriod wp = _day_periods[day][pname];

        // temperature
        string temperature_s = temperature_divs[period].Text.Trim().Replace('−', '-');
        w.TemperatureLow = w.TemperatureHigh = int.Parse(temperature_s);

        // weather type
        string cn = "icon-weather icon-weather-";
        Element e = weather_divs[period].Element(Find.ByClass(new StringStartsWith() { startswith = cn }));
        if (e == null || !e.Exists)
        {
          _error_descr = "cant find weather type";
          return false;
        }
        string wt = e.ClassName.Substring(cn.Length);
        if (wt.IndexOf(' ') != -1)
          wt = wt.Substring(0, wt.IndexOf(' '));

        w.WeatherType = weather_type_encoding.Keys.Contains(wt) ? weather_type_encoding[wt] : WeatherType.Undefined;

        // wind
        cn = "icon-small icon-wind-";
        e = wind_divs[period].Element(Find.ByClass(new StringStartsWith() { startswith = cn }));
        if (e == null || !e.Exists)
        {
          _error_descr = "cant find wind direction";
          return false;
        }

        string wd = e.ClassName.Substring(cn.Length);
        if (wd.IndexOf(' ') != -1)
          wd = wd.Substring(0, wd.IndexOf(' '));

        w.WindDirection = wind_direction_encoding.Keys.Contains(wd) ? wind_direction_encoding[wd] : WindDirection.Undefined;

        string ws = wind_divs[period].Text.TrimStart('\n', '\r', '\t', ' ');
        ws = ws.Substring(0, ws.IndexOf(' '));
        w.WindSpeed = int.Parse(ws);

        // pressure
        string pr = pressure_divs[period].Text.TrimStart('\n', '\r', '\t', ' ');
        pr = pr.Substring(0, pr.IndexOf(' '));
        w.Pressure = int.Parse(pr);

        // humidity
        string hu = humidity_divs[period].Text;
        hu = hu.Substring(0, hu.IndexOf('%'));
        w.Humidity = int.Parse(hu);

        _weather[wp] = w;
      }

      return true;
    }

    private void read_ngs_current_weather(weather w)
    {
      bool success = true;
      _succeeded = true;

      try
      {
        Div info = browser_.Div(Find.ByClass("today-panel__info"));
        if (!info.Exists)
        {
          _error_descr = "incorrect structure";
          success = false;
        }
        else
        {
          // weather character
          string class_name = "icon-weather-big ";
          Div icon_weather = info.Div(Find.ByClass(new StringStartsWith() { startswith = class_name }));
          if (!icon_weather.Exists)
          {
            _error_descr = "incorrect structure";
            success = false;
          }
          else
          { 
            string wt = icon_weather.ClassName.Substring(class_name.Length);
            w.WeatherType = weather_type_encoding.Keys.Contains(wt) ? weather_type_encoding[wt] : WeatherType.Undefined;
          }

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
              string st = temp.Text.Trim().Replace(',', '.').Replace('−', '-');
              if (string.IsNullOrEmpty(st))
              {
                _error_descr = "incorrect structure 1";
                success = false;
              }
              else
              {
                double t = double.Parse(st);
                w.TemperatureHigh = w.TemperatureLow = t;
              }
            }

            // wind
            string cn = "icon-small icon-wind-";
            string txt = curr.Text;
            Element ei = curr.Element(Find.ByClass(new StringStartsWith() { startswith = cn }));
            if (ei == null || !ei.Exists || ei.TagName.ToLower() != "i")
            {
              _error_descr = "incorrect structure 2";
              success = false;
            }
            else
            {
              string wd = ei.ClassName.Substring(cn.Length);
              w.WindDirection = wind_direction_encoding.Keys.Contains(wd) ? wind_direction_encoding[wd] : WindDirection.Undefined;

              Element edt = ei.Parent.NextSibling;
              if (edt.TagName.ToLower() != "dt")
              {
                _error_descr = "incorrect structure 2.1";
                success = false;
              }
              else
              {
                string wind = edt.Text.TrimStart(' ');
                double ws;
                if (double.TryParse(wind.Substring(0, wind.IndexOf(' ')), out ws))
                  w.WindSpeed = ws;
              }
            }

            // pressure 
            ei = curr.Element(Find.ByClass("icon-small icon-pressure"));
            if (ei == null || !ei.Exists || ei.TagName.ToLower() != "i")
            {
              _error_descr = "incorrect structure 2.2";
              success = false;
            }
            else
            {
              double p;
              string pr = ei.GetAttributeValue("title");
              if (double.TryParse(pr.Substring(0, pr.IndexOf(' ')), out p))
                w.Pressure = p;
            }

            // humidity
            ei = curr.Element(Find.ByClass("icon-small icon-humidity"));
            if (ei == null || !ei.Exists || ei.TagName.ToLower() != "i")
            {
              _error_descr = "incorrect structure 2.3";
              success = false;
            }
            else
            {
              double h;
              string humidity = ei.GetAttributeValue("title");
              if (double.TryParse(humidity.Substring(0, humidity.IndexOf('%')), out h))
                w.Humidity = h;
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
