using System;
using System.Collections.Generic;
using System.Linq;

using System.Threading;
using System.Xml;

namespace weather
{
  public class weather
  {
    object _lock = new object();

    private double? _temperature_low = null;
    private double? _temperature_high = null;
    public double? _pressure = null;
    public double? _humidity = null;
    public double? _wind_speed = null;
    public WindDirection _wind_direction = WindDirection.Undefined;
    public WeatherType _character = WeatherType.Undefined;

    public double? TemperatureLow { get { lock (_lock) { return _temperature_low; } } set { lock (_lock) { _temperature_low = value; } } }
    public double? TemperatureHigh { get { lock (_lock) { return _temperature_high; } } set { lock (_lock) { _temperature_high = value; } } }
    public double? Pressure { get { lock (_lock) { return _pressure; } } set { lock (_lock) { _pressure = value; } } }
    public double? Humidity { get { lock (_lock) { return _humidity; } } set { lock (_lock) { _humidity = value; } } }
    public double? WindSpeed { get { lock (_lock) { return _wind_speed; } } set { lock (_lock) { _wind_speed = value; } } }
    public WindDirection WindDirection { get { lock (_lock) { return _wind_direction; } } set { lock (_lock) { _wind_direction = value; } } }
    public WeatherType WeatherType { get { lock (_lock) { return _character; } } set { lock (_lock) { _character = value; } } }
  }


  public abstract class WeatherProviderBase : IWeatherProvider
  {
    protected Thread _reader;
    protected AutoResetEvent _exit = new AutoResetEvent(false);

    protected Object _locker = new Object();
    protected XmlNamespaceManager _nsmgr;
    protected Dictionary<WeatherPeriod, weather> _weather = new Dictionary<WeatherPeriod, weather>();
    protected string _error_descr = "";
    protected bool _succeeded = false;


    public WeatherProviderBase()
    {
      _reader = new Thread(new ThreadStart(readdata)) { IsBackground = true };
      _reader.SetApartmentState(ApartmentState.STA);
      _reader.Start();
    }

    public abstract int release();

    protected virtual void close()
    {
      _exit.Set();
    }

    public virtual string get_error_description()
    {
      return _error_descr;
    }

    public virtual bool get_status()
    {
      return _succeeded;
    }

    public virtual bool get_temperature(WeatherPeriod period, out double temp_l, out double temp_h)
    {
      lock (_locker)
      {
        if (_weather.ContainsKey(period) && _weather[period].TemperatureLow != null)
        {
          temp_l = (double)_weather[period].TemperatureLow;
          temp_h = (double)(_weather[period].TemperatureHigh ?? _weather[period].TemperatureLow);
          return true;
        }

        temp_l = temp_h = 0.0;
        return false;
      }
    }

    public virtual bool get_pressure(WeatherPeriod period, out double pressure)
    {
      lock (_locker)
      {
        if (_weather.ContainsKey(period) && _weather[period].Pressure != null)
        {
          pressure = (double)_weather[period].Pressure;
          return true;
        }

        pressure = 0.0;
        return false;
      }
    }

    public virtual bool get_humidity(WeatherPeriod period, out double hum)
    {
      lock (_locker)
      {
        if (_weather.ContainsKey(period) && _weather[period].Humidity != null)
        {
          hum = (double)_weather[period].Humidity;
          return true;
        }

        hum = 0.0;
        return false;
      }
    }

    public virtual bool get_wind(WeatherPeriod period, out WindDirection direction, out double speed)
    {
      lock (_locker)
      {
        if (_weather.ContainsKey(period) && _weather[period].WindSpeed != null)
        {
          direction = _weather[period].WindDirection;
          speed = (double)_weather[period].WindSpeed;
          return true;
        }

        direction = WindDirection.Undefined;
        speed = 0.0;
        return false;
      }
    }

    public virtual bool get_character(WeatherPeriod period, out WeatherType type)
    {
      lock (_locker)
      {
        if (_weather.ContainsKey(period))
        {
          type = _weather[period].WeatherType;
          return true;
        }

        type = WeatherType.Undefined;
        return false;
      }
    }

    protected abstract void readdata();
  }
}
