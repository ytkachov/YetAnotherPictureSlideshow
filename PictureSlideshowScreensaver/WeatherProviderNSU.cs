using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using System.Xml;
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

  public static class XmlExtensions
  {
    public static XmlNodeList SelectCellDivs(this XmlNode tr, string selector)
    {
      XmlNode tc = tr.SelectSingleNode(string.Format("./td[@class='{0}']", selector));
      if (tc == null)
        throw new Exception("cant find requested table cell");

      return tc.SelectNodes("./div");
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
        browser_.GoToNoWait("http://pogoda.ngs.ru/academgorodok/");
        Thread.Sleep(5000);

        XmlDocument pg = new XmlDocument();

        Table tbl = browser_.Table(Find.ByClass("pgd-detailed-cards elements"));
        string outerhtml = tbl.OuterHtml.Replace("&nbsp;", " ");

        pg.LoadXml(outerhtml);

        XmlNode pgd_detailed = pg.DocumentElement;
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

    private bool extract_3days_forecast(XmlNode short_forecast)
    {
      XmlNodeList days = short_forecast.SelectNodes("./tbody/tr");
      if (days.Count != 3)
      {
        _error_descr = "incorrect days number in forecast";
        return false;
      }

      for (int day = 0; day < 3; day++)
      {
        XmlNode tr = days[day];
        if (!extract_day_forecast(day, tr))
          return false;
      }

      return true;
    }

    private bool extract_day_forecast(int day, XmlNode tr)
    {
      XmlNode tc = tr.SelectSingleNode("./td[@class='elements__section-day']");
      if (tc == null)
      {
        _error_descr = "cant find day " + day.ToString();
        return false;
      }

      // check day of month
      XmlNodeList spans = tc.SelectNodes("./div/span");
      if (spans.Count != 1)
      {
        _error_descr = "incorrect days in day" + day.ToString();
        return false;
      }

      string dt = spans[0].InnerText;
      int di;
      if (!int.TryParse(dt.Substring(0, dt.IndexOf(' ')), out di))
        di = 0;

      if ((DateTime.Now + TimeSpan.FromDays(day)).Day != di)
      {
        _error_descr = "incorrect day";
        return false;
      }

      // day's periods
      XmlNodeList period_divs = tr.SelectCellDivs("elements__section-daytime");

      // temperature
      XmlNodeList temperature_divs = tr.SelectCellDivs("elements__section-temperature");
      if (temperature_divs.Count != period_divs.Count)
        throw new Exception(string.Format("incorrect temperature count in day {0}", day));

      // weather type
      XmlNodeList weather_divs = tr.SelectCellDivs("elements__section-weather");
      if (weather_divs.Count != period_divs.Count)
        throw new Exception(string.Format("incorrect weather type count in day {0}", day));

      // wind
      XmlNodeList wind_divs = tr.SelectCellDivs("elements__section-wind");
      if (wind_divs.Count != period_divs.Count)
        throw new Exception(string.Format("incorrect wind count in day {0}", day));

      // pressure
      XmlNodeList pressure_divs = tr.SelectCellDivs("elements__section-pressure");
      if (pressure_divs.Count != period_divs.Count)
        throw new Exception(string.Format("incorrect pressure count in day {0}", day));

      // humidity
      XmlNodeList humidity_divs = tr.SelectCellDivs("elements__section-humidity");
      if (humidity_divs.Count != period_divs.Count)
        throw new Exception(string.Format("incorrect humidity count in day {0}", day));

      int pcount = period_divs.Count;
      for (int period = 0; period < pcount; period++)
      {
        weather w = new weather();

        // weather period
        string pname = period_divs[period].InnerText;
        if (!_day_periods[day].Keys.Contains(pname))
          throw new Exception("invalid period name");

        WeatherPeriod wp = _day_periods[day][pname];

        // temperature
        string temperature_s = temperature_divs[period].InnerText.Trim().Replace('−', '-');
        w.TemperatureLow = w.TemperatureHigh = int.Parse(temperature_s);

        // weather type
        string cn = "icon-weather icon-weather-";
        XmlNode e = weather_divs[period].SelectSingleNode("./i");
        if (e == null)
          throw new Exception("cant find weather type");

        string wt = e.SelectSingleNode("@class").Value.Substring(cn.Length);
        if (wt.IndexOf(' ') != -1)
          wt = wt.Substring(0, wt.IndexOf(' '));

        w.WeatherType = weather_type_encoding.Keys.Contains(wt) ? weather_type_encoding[wt] : WeatherType.Undefined;

        // wind
        cn = "icon-small icon-wind-";
        e = wind_divs[period].SelectSingleNode("./i");
        if (e == null)
          throw new Exception("cant find wind direction");

        string wd = e.SelectSingleNode("@class").Value.Substring(cn.Length);
        if (wd.IndexOf(' ') != -1)
          wd = wd.Substring(0, wd.IndexOf(' '));

        w.WindDirection = wind_direction_encoding.Keys.Contains(wd) ? wind_direction_encoding[wd] : WindDirection.Undefined;

        string ws = wind_divs[period].InnerText.TrimStart('\n', '\r', '\t', ' ');
        ws = ws.Substring(0, ws.IndexOf(' '));
        w.WindSpeed = int.Parse(ws);

        // pressure
        string pr = pressure_divs[period].InnerText.TrimStart('\n', '\r', '\t', ' ');
        pr = pr.Substring(0, pr.IndexOf(' '));
        w.Pressure = int.Parse(pr);

        // humidity
        string hu = humidity_divs[period].InnerText;
        hu = hu.Substring(0, hu.IndexOf('%'));
        w.Humidity = int.Parse(hu);

        _weather[wp] = w;
      }

      return true;
    }

    private XmlNodeList get_all_divs(XmlNode tr, string v)
    {
      throw new NotImplementedException();
    }

    private void read_ngs_current_weather(weather w)
    {
      bool success = true;
      _succeeded = true;

      try
      {
        browser_.GoToNoWait("http://pogoda.ngs.ru/academgorodok/");
        Thread.Sleep(5000);

        XmlDocument pg = new XmlDocument();

        Div info = browser_.Div(Find.ByClass("today-panel__info"));
        if (!info.Exists)
          throw new Exception("incorrect current weather structure ");

        string outerhtml = info.OuterHtml.Replace("&nbsp;", " ");
        pg.LoadXml(outerhtml);

        XmlNode pgd_current = pg.DocumentElement;

        // weather character
        string class_name = "icon-weather-big ";
        XmlNode icon_weather = pgd_current.SelectSingleNode(string.Format("./div/div[starts-with(@class, '{0}')]", class_name));
        if (icon_weather == null)
          throw new Exception("incorrect current weather structure: cant find weather type icon");

        string wt = icon_weather.SelectSingleNode("@class").Value.Substring(class_name.Length);
        w.WeatherType = weather_type_encoding.Keys.Contains(wt) ? weather_type_encoding[wt] : WeatherType.Undefined;

        XmlNode curr = pgd_current.SelectSingleNode("./div/div[@class = 'today-panel__info__main__item first']");
        if (curr == null)
          throw new Exception("incorrect current weather structure: cant find weather panel");

        // temperature
        XmlNode temp = curr.SelectSingleNode("./div/span/span[@class = 'value__main']");
        if (temp == null)
          throw new Exception("incorrect current weather structure: cant find current temperature");

        string st = temp.InnerText.Trim().Replace(',', '.').Replace('−', '-');
        if (string.IsNullOrEmpty(st))
          throw new Exception("incorrect current weather structure: incorrect current temperature string");
        else
        {
          double t = double.Parse(st);
          w.TemperatureHigh = w.TemperatureLow = t;
        }

        File.WriteAllText(@"D:\Projects\YetAnotherPictureSlideshow\PictureSlideshowScreensaver\samples\XMLFile2.xml", curr.OuterXml);

        // wind
        string cn = "icon-small icon-wind-";
        XmlNode ei = curr.SelectSingleNode(string.Format("./dl/dd/i[starts-with(@class, '{0}')]", cn));
        if (ei == null)
          throw new Exception("incorrect current weather structure: cant find wind direction");
        else
        {
          string wd = ei.SelectSingleNode("@class").Value.Substring(cn.Length);
          w.WindDirection = wind_direction_encoding.Keys.Contains(wd) ? wind_direction_encoding[wd] : WindDirection.Undefined;

          XmlNode edt = ei.ParentNode.NextSibling;
          if (edt == null || edt.Name.ToLower() != "dt")
          {
            _error_descr = "incorrect structure 2.1";
            success = false;
          }
          else
          {
            string wind = edt.InnerText.Replace("\n", " ").TrimStart(' ');
            double ws;
            if (double.TryParse(wind.Substring(0, wind.IndexOf(' ')), out ws))
              w.WindSpeed = ws;
          }
        }

        // pressure 
        ei = curr.SelectSingleNode("./dl/dd/i[@class = 'icon-small icon-pressure']");
        if (ei == null ||  ei.Name.ToLower() != "i")
        {
          _error_descr = "incorrect structure 2.2";
          success = false;
        }
        else
        {
          double p;
          string pr = ei.SelectSingleNode("@title").Value;
          if (double.TryParse(pr.Substring(0, pr.IndexOf(' ')), out p))
            w.Pressure = p;
        }

        // humidity
        ei = curr.SelectSingleNode("./dl/dd/i[@class = 'icon-small icon-humidity']");
        if (ei == null || ei.Name.ToLower() != "i")
        {
          _error_descr = "incorrect structure 2.3";
          success = false;
        }
        else
        {
          double h;
          string humidity = ei.SelectSingleNode("@title").Value;
          if (double.TryParse(humidity.Substring(0, humidity.IndexOf('%')), out h))
            w.Humidity = h;
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
