using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;
using weather;

namespace informers
{

  class weatherformatter
  {
    static public Dictionary<WeatherType, string[]> weather_types_to_picture = new Dictionary<WeatherType, string[]>()
    {
      { WeatherType.Clear,                  new string [] { "wt_clear_d", "wt_clear_n" } },
      { WeatherType.PartlyCloudy,           new string [] { "wt_partly_cloudy_d", "wt_partly_cloudy_n" } },
      { WeatherType.Cloudy,                 new string [] { "wt_cloudy_d", "wt_cloudy_n" } },
      { WeatherType.CloudyPartlyRainy,      new string [] { "wt_cloudy_partly_rainy_d", "wt_cloudy_partly_rainy_n" } },
      { WeatherType.CloudyPartlySnowy,      new string [] { "wt_cloudy_partly_snowy_d", "wt_cloudy_partly_snowy_n" } },
      { WeatherType.CloudyRainy,            new string [] { "wt_cloudy_rainy_d", "wt_cloudy_rainy_n" } },
      { WeatherType.CloudySnowy,            new string [] { "wt_cloudy_snowy_d", "wt_cloudy_snowy_n" } },
      { WeatherType.CloudyRainyStorm,       new string [] { "wt_cloudy_rainy_storm_d", "wt_cloudy_rainy_storm_d" } },
      { WeatherType.CloudySnowyStorm,       new string [] { "wt_cloudy_snowy_storm_d", "wt_cloudy_snowy_storm_n" } },
      { WeatherType.Overcast,               new string [] { "wt_overcast", "wt_overcast" } },
      { WeatherType.OvercastPartlyRainy,    new string [] { "wt_overcast_partly_rainy", "wt_overcast_partly_rainy" } },
      { WeatherType.OvercastPartlySnowy,    new string [] { "wt_overcast_partly_snowy", "wt_overcast_partly_snowy" } },
      { WeatherType.OvercastRainy,          new string [] { "wt_overcast_rainy", "wt_overcast_rainy" } },
      { WeatherType.OvercastSnowy,          new string [] { "wt_overcast_snowy", "wt_overcast_snowy" } },
      { WeatherType.OvercastLightningRainy, new string [] { "wt_overcast_rainy_storm_lightning", "wt_overcast_rainy_storm_lightning" } },
      { WeatherType.OvercastRainyStorm,     new string [] { "wt_overcast_rainy_storm", "wt_overcast_rainy_storm" } },
      { WeatherType.OvercastSnowyStorm,     new string [] { "wt_overcast_snowy_storm", "wt_overcast_snowy_storm" } },
      { WeatherType.Undefined,              new string [] { "undefined", "undefined" } }
    };

    static public Dictionary<WindDirection, string> wind_direction_to_picture = new Dictionary<WindDirection, string>()
    {
      { WindDirection.Undefined,    "wd_udefined" },
      { WindDirection.N,            "wd_N"    },
      { WindDirection.NNE,          "wd_NNE"  },
      { WindDirection.NE,           "wd_NE"   }, 
      { WindDirection.ENE,          "wd_ENE"  },
      { WindDirection.E,            "wd_E"    },  
      { WindDirection.ESE,          "wd_ESE"  },
      { WindDirection.SE,           "wd_SE"   }, 
      { WindDirection.SSE,          "wd_SSE"  },
      { WindDirection.S,            "wd_S"    },  
      { WindDirection.SSW,          "wd_SSW"  },
      { WindDirection.SW,           "wd_SW"   }, 
      { WindDirection.WSW,          "wd_WSW"  },
      { WindDirection.W,            "wd_W"    },   
      { WindDirection.WNW,          "wd_WNW"  },
      { WindDirection.NW,           "wd_NW"   }, 
      { WindDirection.NNW,          "wd_NNW"  }
    };

  }

  public class WeatherToPicture : MarkupExtension, IValueConverter
  {
    public string UseColor { get; set; }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      if (value == null)
        return null;

      WeatherType wt = (WeatherType)value;
      return Application.Current.TryFindResource(weatherformatter.weather_types_to_picture[wt][0]) as Canvas;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
      throw new NotImplementedException();
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
      return this;
    }
  }

  public class WindDirectionToPicture : MarkupExtension, IValueConverter
  {
    public string UseColor { get; set; }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      if (value == null)
        return null;

      WindDirection wd = (WindDirection)value;
      return Application.Current.TryFindResource(weatherformatter.wind_direction_to_picture[wd]) as Canvas;
      //return Application.Current.TryFindResource("wd_E") as Canvas;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
      throw new NotImplementedException();
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
      return this;
    }
  }

  public class WeatherStatusToOpacity : MarkupExtension, IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      bool ws = (bool)value;
      // return 1;
      return ws ? 1 : 0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
      throw new NotImplementedException();
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
      return this;
    }
  }

  public class WeatherInformer : INotifyPropertyChanged
  {
    private DispatcherTimer _weatherTick = new DispatcherTimer();

    private IWeatherProvider _curr_temp_provider = WeatherProviderNSU.get();
    private IWeatherProvider _forecast_provider = WeatherProviderYandex.get();

    private bool _weather_status = false;
    private double _temperature = 0.0, _temperature_low, _temperature_high;
    private double _wind_speed = 0.0;
    private WindDirection _wind_direction = WindDirection.Undefined;
    private double _humidity = 0.0;
    private WeatherType _weather_type = WeatherType.Undefined;
    private double _pressure = 0.0;

    private WeatherPeriod _weather_period = WeatherPeriod.Undefined;

    internal WeatherPeriod Weather_Period  { get { return _weather_period; } set { _weather_period = value; update_Weather(); } }

    public string Temperature
    {
      get { return (_temperature >= 0 ? "+" : "") + _temperature.ToString(); }
      set { _temperature = double.Parse(value); RaisePropertyChanged("Temperature"); }
    }

    public string TemperatureRange
    {
      get
      {
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

    public bool Weather_Status { get { return _weather_status; } set { _weather_status = value; RaisePropertyChanged("Weather_Status"); } }
    public double Pressure { get { return _pressure; } set { _pressure = value; RaisePropertyChanged("Pressure"); } }
    public double Humidity { get { return _humidity; } set { _humidity = value; RaisePropertyChanged("Humidity"); } }
    public double WindSpeed { get { return _wind_speed; } set { _wind_speed = value; RaisePropertyChanged("WindSpeed"); } }
    public WindDirection WindDirection { get { return _wind_direction; } set { _wind_direction = value; RaisePropertyChanged("WindDirection"); } }
    public WeatherType Weather { get { return _weather_type; } set { _weather_type = value; RaisePropertyChanged("Weather"); } }

    private void RaisePropertyChanged(string propertyName)
    {
      if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
    }

    public WeatherInformer()
    {
      _weatherTick.Tick += new EventHandler(weather_Tick);
      _weatherTick.Interval = TimeSpan.FromSeconds(5.0);
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
    }

    private void update_Weather(IWeatherProvider provider, WeatherPeriod period)
    {
      WeatherType w;
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
