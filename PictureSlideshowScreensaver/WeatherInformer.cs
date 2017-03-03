using System;
using System.ComponentModel;
using System.Windows.Threading;

using weather;

namespace informers
{

  public class WeatherInformer : INotifyPropertyChanged
  {
    private DispatcherTimer _weatherTick = new DispatcherTimer();

    private IWeatherProvider _curr_temp_provider = WeatherProviderNGS.get();
    private IWeatherProvider _forecast_provider = WeatherProviderNGS.get();

    private string _dbg_info = "";
    private bool _weather_status = false;
    private double _temperature = 0.0, _temperature_low, _temperature_high;
    private double _wind_speed = 0.0;
    private WindDirection _wind_direction = WindDirection.Undefined;
    private double _humidity = 0.0;
    private WeatherType _weather_type = WeatherType.Undefined;
    private double _pressure = 0.0;

    private WeatherPeriod _weather_period = WeatherPeriod.Undefined;

    public string Temperature
    {
      get { return (_temperature >= 0 ? "+" : "") + _temperature.ToString(); }
      set { _temperature = double.Parse(value); RaisePropertyChanged("Temperature"); }
    }

    public string DbgInfo
    {
      get { return _dbg_info; }
      set { _dbg_info = value; RaisePropertyChanged("DbgInfo"); }
    }

    public string TemperatureRange
    {
      get
      {
        if (_temperature_low == _temperature_high)
          return ((_temperature_low >= 0 ? "+" : "") + _temperature_low.ToString());
        else
          return ((_temperature_low >= 0 ? "+" : "") + _temperature_low.ToString()) + ".." +
               ((_temperature_high >= 0 ? "+" : "") + _temperature_high.ToString());
      }
      set
      {
        string [] temps = value.Split("|".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
        _temperature_low = double.Parse(temps[0]);
        _temperature_high = double.Parse(temps[1]);
        RaisePropertyChanged("TemperatureRange");
      }
    }

    public double Pressure { get { return _pressure; } set { _pressure = value; RaisePropertyChanged("Pressure"); } }
    public double Humidity { get { return _humidity; } set { _humidity = value; RaisePropertyChanged("Humidity"); } }
    public double WindSpeed { get { return _wind_speed; } set { _wind_speed = value; RaisePropertyChanged("WindSpeed"); } }
    public WindDirection WindDirection { get { return _wind_direction; } set { _wind_direction = value; RaisePropertyChanged("WindDirection"); } }

    public WeatherType Weather { get { return _weather_type; }
      set {
        _weather_type = value;
        RaisePropertyChanged("Weather"); }
    }

    public WeatherPeriod Weather_Period { get { return _weather_period; } set { _weather_period = value; update_Weather(); RaisePropertyChanged("Weather_Period"); } }
    public bool Weather_Status { get { return _weather_status; } set { _weather_status = value; RaisePropertyChanged("Weather_Status"); } }

    private void RaisePropertyChanged(string propertyName)
    {
      if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
    }

    public WeatherInformer()
    {
      _weatherTick.Tick += new EventHandler(weather_Tick);
      _weatherTick.Interval = TimeSpan.FromSeconds(15.0);
      _weatherTick.Start();
    }

    void weather_Tick(object sender, EventArgs e)
    {
      update_Weather();
    }

    private void update_Weather()
    {
      update_Temperature(Weather_Period == WeatherPeriod.Now ? _curr_temp_provider : _forecast_provider, Weather_Period);
      update_Pressure(Weather_Period == WeatherPeriod.Now ? _curr_temp_provider : _forecast_provider, Weather_Period);
      update_Wind(Weather_Period == WeatherPeriod.Now ? _curr_temp_provider : _forecast_provider, Weather_Period);
      update_Humidity(Weather_Period == WeatherPeriod.Now ? _curr_temp_provider : _forecast_provider, Weather_Period);
      update_Weather(Weather_Period == WeatherPeriod.Now ? _curr_temp_provider : _forecast_provider, Weather_Period);
      update_DbgInfo(Weather_Period == WeatherPeriod.Now ? _curr_temp_provider : _forecast_provider);
    }

    private void update_Weather(IWeatherProvider provider, WeatherPeriod period)
    {
      WeatherType w;
      if (period == WeatherPeriod.TomorrowNight || period == WeatherPeriod.TomorrowMorning)
      {
        int i = 0;
      }

      if (provider.get_character(period, out w))
      {
        Weather = w;
        Weather_Status = true;
      }
      else
      {
        Weather_Status = false;
      }


    }

    private void update_Temperature(IWeatherProvider provider, WeatherPeriod period)
    {
      double temp_l, temp_h;
      if (provider.get_temperature(period, out temp_l, out temp_h))
      {
        Temperature = ((temp_l + temp_h) / 2.0).ToString();
        TemperatureRange = temp_l + "|" + temp_h;

        Weather_Status = true;
      }
      else
      {
        Weather_Status = false;
      }
    }

    private void update_Pressure(IWeatherProvider provider, WeatherPeriod period)
    {
      double press;
      if (provider.get_pressure(period, out press))
      {
        Pressure = press;
        Weather_Status = true;
      }
      else
      {
        Weather_Status = false;
      }
    }

    private void update_DbgInfo(IWeatherProvider provider)
    {
      DbgInfo = "this is dbg_info"; // provider.get_error_description();
    }

    private void update_Humidity(IWeatherProvider provider, WeatherPeriod period)
    {
      double hum;
      if (provider.get_humidity(period, out hum))
      {
        Humidity = hum;
        Weather_Status = true;
      }
      else
      {
        Weather_Status = false;
      }
    }

    private void update_Wind(IWeatherProvider provider, WeatherPeriod period)
    {
      double ws;
      WindDirection wd;
      if (provider.get_wind(period, out wd, out ws))
      {
        WindDirection = wd;
        WindSpeed = ws;
        Weather_Status = true;
      }
      else
      {
        Weather_Status = false;
      }
    }

    public void Close()
    {
      _curr_temp_provider.release();
      _forecast_provider.release();
    }

    // INotifyPropertyChanged
    public event PropertyChangedEventHandler PropertyChanged;
  }
}
