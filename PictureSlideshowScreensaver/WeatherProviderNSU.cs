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
    private int _temperature = 0;
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

    public override bool get_temperature(WeatherPeriod time, out double temp_l, out double temp_h)
    {
      if (time != WeatherPeriod.Now)
      {
        temp_l = temp_h = 0;
        return false;
      }

      lock (_locker)
      {
        temp_l = temp_h = _temperature;
        return _succeeded;
      }
    }

    public override bool get_pressure(WeatherPeriod time, out double pressure) { pressure = 0.0;  return false; }
    public override bool get_humidity(WeatherPeriod time, out double hum) { hum = 0.0; return false; }
    public override bool get_wind(WeatherPeriod time, out WindDirection direction, out double speed) { direction = WindDirection.Undefined; speed = 0.0;  return false; }
    public override bool get_character(WeatherPeriod time, out WeatherType type) { type = WeatherType.Undefined; return false; }

    protected override void readdata()
    {
      Settings.AutoMoveMousePointerToTopLeft = false;
      Settings.MakeNewIeInstanceVisible = false;
      browser_ = new IE();

      while (true)
      {
        bool success = false;
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
              _temperature = (int)(t + 0.5);
              _succeeded = true;
            }
          }
        }
             
        if (!success)
        {
          lock (_locker)
          {
            _succeeded = false;
          }
        }

        if (_exit.WaitOne(TimeSpan.FromMinutes(10)))
          break;
      }
    }
  }
}
