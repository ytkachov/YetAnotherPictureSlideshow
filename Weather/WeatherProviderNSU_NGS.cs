using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;

namespace weather
{

  public class WeatherProviderNGS : WeatherProviderBase
  {
    private static IWeatherProvider _self = null;
    private static int _refcounter = 0;

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
      { "partly_cloudy_rain_with_snow_day",   WeatherType.CloudyPartlyRainy },
      { "partly_cloudy_snow_day",             WeatherType.CloudySnowy },
      { "partly_cloudy_light_snow_day",       WeatherType.CloudyPartlySnowy },
      { "partly_cloudy_thunderstorm_day",     WeatherType.CloudyLightningRainy },
      { "light_cloudy_none_day",              WeatherType.PartlyCloudy},
      { "partly_cloudy_none_day",             WeatherType.PartlyCloudy},
      { "partly_cloudy_rainless_day" ,        WeatherType.PartlyCloudy},
      { "mostly_cloudy_rain_day",             WeatherType.CloudyRainy },
      { "mostly_cloudy_light_rain_day",       WeatherType.CloudyPartlyRainy },
      { "mostly_cloudy_snow_day",             WeatherType.CloudySnowy },
      { "mostly_cloudy_light_snow_day",       WeatherType.CloudyPartlySnowy },
      { "mostly_cloudy_thunderstorm_day",     WeatherType.CloudyLightningRainy },
      { "mostly_cloudy_none_day",             WeatherType.Cloudy },
      { "mostly_cloudy_sleet_day",            WeatherType.OvercastPartlySnowy },
      { "cloudy_rain_day",                    WeatherType.OvercastRainy },
      { "cloudy_rainless_day",                WeatherType.Overcast },
      { "cloudy_light_rain_day",              WeatherType.OvercastPartlyRainy },
      { "cloudy_sleet_day",                   WeatherType.OvercastPartlyRainy },
      { "cloudy_snow_with_rain_day",          WeatherType.OvercastPartlyRainy },
      { "cloudy_snow_day",                    WeatherType.OvercastSnowy },
      { "cloudy_light_snow_day",              WeatherType.OvercastPartlySnowy },
      { "cloudy_heavy_snow_day",              WeatherType.OvercastSnowyStorm },
      { "cloudy_thunderstorm_day",            WeatherType.OvercastLightningRainy },
      { "cloudy_none_day",                    WeatherType.Overcast },
      { "sunshine_light_rain_night",          WeatherType.ClearPartlyRainy },
      { "sunshine_light_snow_night",          WeatherType.ClearPartlySnowy },
      { "sunshine_rain_night",                WeatherType.ClearRainy },
      { "sunshine_none_night",                WeatherType.Clear },
      { "partly_cloudy_rain_night",           WeatherType.CloudyRainy },
      { "partly_cloudy_light_rain_night",     WeatherType.CloudyPartlyRainy },
      { "partly_cloudy_rain_with_snow_night", WeatherType.CloudyPartlyRainy },
      { "partly_cloudy_snow_night",           WeatherType.CloudySnowy },
      { "partly_cloudy_light_snow_night",     WeatherType.CloudyPartlySnowy },
      { "partly_cloudy_thunderstorm_night",   WeatherType.CloudyLightningRainy },
      { "light_cloudy_none_night",            WeatherType.PartlyCloudy},
      { "partly_cloudy_none_night",           WeatherType.PartlyCloudy},
      { "partly_cloudy_rainless_night" ,      WeatherType.PartlyCloudy},
      { "mostly_cloudy_rain_night",           WeatherType.CloudyRainy },
      { "mostly_cloudy_light_rain_night",     WeatherType.CloudyPartlyRainy },
      { "mostly_cloudy_snow_night",           WeatherType.CloudySnowy },
      { "mostly_cloudy_light_snow_night",     WeatherType.CloudyPartlySnowy },
      { "mostly_cloudy_thunderstorm_night",   WeatherType.CloudyLightningRainy },
      { "mostly_cloudy_none_night",           WeatherType.Cloudy },
      { "mostly_cloudy_sleet_night",          WeatherType.OvercastPartlySnowy },
      { "cloudy_rain_night",                  WeatherType.OvercastRainy },
      { "cloudy_rainless_night",              WeatherType.Overcast },
      { "cloudy_light_rain_night",            WeatherType.OvercastPartlyRainy },
      { "cloudy_sleet_night",                 WeatherType.OvercastPartlyRainy },
      { "cloudy_snow_with_rain_night",        WeatherType.OvercastPartlyRainy },
      { "cloudy_snow_night",                  WeatherType.OvercastSnowy },
      { "cloudy_light_snow_night",            WeatherType.OvercastPartlySnowy },
      { "cloudy_heavy_snow_night",            WeatherType.OvercastSnowyStorm },
      { "cloudy_thunderstorm_night",          WeatherType.OvercastLightningRainy },
      { "cloudy_none_night",                  WeatherType.Overcast }
    };

    private IWeatherReader _sitereader = null;
    private WeatherProviderNGS()
    {
      _sitereader = new NGSFileReaderWriter(WeatherSource.NC);
    }

    public static IWeatherProvider get()
    {
      if (_self == null)
        _self = new WeatherProviderNGS();

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
      _sitereader.close();
      base.close();
    }


    private void read_nsu_current_temp(WeatherInfo w)
    {
      bool success = false;
      try
      {
        string st = _sitereader.temperature();
        if (st != null || !st.Contains("°"))
        {
          success = true;
          CultureInfo culture = new CultureInfo("en");
          double t = double.Parse(st.Substring(0, st.IndexOf("°")), culture);
          lock (_locker)
          {
            w.TemperatureLow = w.TemperatureHigh = t;
            _succeeded = true;
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

      try
      {
        _sitereader.getrest();
      }
      catch (Exception)
      {

      }
    }

    private void read_ngs_forecast()
    {
      bool success = true;
      _succeeded = true;

      try
      {
        string forecast = _sitereader.forecast();
        if (string.IsNullOrEmpty(forecast))
          throw new Exception("NGS forecast: can't find 3 day forecast table");

        XmlDocument pg = new XmlDocument();
        pg.LoadXml(forecast);

        XmlNode pgd_detailed = pg.DocumentElement;
        extract_3days_forecast(pgd_detailed);
      }
      catch (Exception e)
      {
        success = false;
        _error_descr = e.Message;

        string fname = string.Format("{0} -- {1}", DateTime.Now.ToString("yyyy_MM_dd HH-mm-ss"), _error_descr);
        //File.WriteAllText(fname, browser_.Html);
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

      try
      {
        _sitereader.getrest();
      }
      catch (Exception e)
      {

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
        WeatherInfo w = new WeatherInfo();

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

        if (w.WeatherType == WeatherType.Undefined)
        {
          int i = 0;
        }

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


        lock (_locker)
        {
          _weather[wp] = w;
        }
      }

      return true;
    }

    private void read_ngs_current_weather(WeatherInfo w)
    {
      bool success = true;
      _succeeded = true;

      try
      {
        string current = _sitereader.current();
        if (string.IsNullOrEmpty(current))
          throw new Exception("incorrect current weather structure ");

        XmlDocument pg = new XmlDocument();
        pg.LoadXml(current);

        XmlNode pgd_current = pg.DocumentElement;

        // weather character
        string class_name = "icon-weather-big ";
        XmlNode icon_weather = pgd_current.SelectSingleNode(string.Format("./div/div[starts-with(@class, '{0}')]", class_name));
        if (icon_weather == null)
          throw new Exception("incorrect current weather structure-- cant find weather type icon");

        string wt = icon_weather.SelectSingleNode("@class").Value.Substring(class_name.Length);
        w.WeatherType = weather_type_encoding.Keys.Contains(wt) ? weather_type_encoding[wt] : WeatherType.Undefined;

        XmlNode today = pgd_current.SelectSingleNode("./div[@class = 'today-panel__info__main']");
        if (today == null)
          throw new Exception("incorrect current weather structure -- cant find today weather panel");

        XmlNode curr = today.SelectSingleNode("./div[starts-with(@class, 'today-panel__info__main__item')]");
        if (curr == null)
          throw new Exception("incorrect current weather structure -- cant find weather panel");

        // temperature
        XmlNode temp = curr.SelectSingleNode("./div/span/span[@class = 'value__main']");
        if (temp == null)
          throw new Exception("incorrect current weather structure -- cant find current temperature");

        string st = temp.InnerText.Trim().Replace(',', '.').Replace('−', '-');
        if (string.IsNullOrEmpty(st))
          throw new Exception("incorrect current weather structure -- incorrect current temperature string");

        double t = double.Parse(st, new CultureInfo("en"));
        w.TemperatureHigh = w.TemperatureLow = t;

        //File.WriteAllText(@"D:\Projects\YetAnotherPictureSlideshow\PictureSlideshowScreensaver\samples\XMLFile2.xml", curr.OuterXml);

        // wind
        string cn = "icon-small icon-wind-";
        XmlNode ei = curr.SelectSingleNode(string.Format("./dl/dd/i[starts-with(@class, '{0}')]", cn));
        if (ei == null)
          throw new Exception("incorrect current weather structure -- cant find wind direction");

        string wd = ei.SelectSingleNode("@class").Value.Substring(cn.Length);
        w.WindDirection = wind_direction_encoding.Keys.Contains(wd) ? wind_direction_encoding[wd] : WindDirection.Undefined;

        XmlNode edt = ei.ParentNode.NextSibling;
        if (edt == null || edt.Name.ToLower() != "dt")
          throw new Exception("incorrect structure -- 2.1");

        string wind = edt.InnerText.TrimStart('\n', '\r', '\t', ' ');
        double ws;
        if (double.TryParse(wind.Substring(0, wind.IndexOf(' ')), NumberStyles.Number, new CultureInfo("en"),  out ws))
          w.WindSpeed = ws;

        // pressure 
        ei = curr.SelectSingleNode("./dl/dd/i[@class = 'icon-small icon-pressure']");
        if (ei == null ||  ei.Name.ToLower() != "i")
          throw new Exception("incorrect structure -- 2.2");

        double p;
        string pr = ei.SelectSingleNode("@title").Value;
        if (double.TryParse(pr.Substring(0, pr.IndexOf(' ')), NumberStyles.Number, new CultureInfo("ru"), out p))
          w.Pressure = p;

        // humidity
        ei = curr.SelectSingleNode("./dl/dd/i[@class = 'icon-small icon-humidity']");
        if (ei == null || ei.Name.ToLower() != "i")
          throw new Exception("incorrect structure -- 2.3");

        double h;
        string humidity = ei.SelectSingleNode("@title").Value;
        if (double.TryParse(humidity.Substring(0, humidity.IndexOf('%')), NumberStyles.Number, new CultureInfo("ru"), out h))
          w.Humidity = h;
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

    protected override void read_current_weather()
    {
      WeatherInfo w = new WeatherInfo();

      read_nsu_current_temp(w);
      read_ngs_current_weather(w);

      lock (_locker)
      {
        _weather.Clear();
        _weather[WeatherPeriod.Now] = w;
      }

    }

    protected override void read_forecast()
    {
      read_ngs_forecast();
    }
  }
}
