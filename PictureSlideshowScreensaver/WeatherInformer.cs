using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;
using Yaps.Core.Abstractions;
using Yaps.Core.Models.Weather;

namespace informers
{

  class WeatherFormatter
  {
    static public readonly FrozenDictionary<WeatherType, string[]> weather_types_to_picture = new Dictionary<WeatherType, string[]>()
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
    }.ToFrozenDictionary();

    static public readonly FrozenDictionary<WindDirection, string> wind_direction_to_picture = new Dictionary<WindDirection, string>()
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
    }.ToFrozenDictionary();

  }

  public class WeatherToPicture : MarkupExtension, IMultiValueConverter
  {
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
      // Bindings traverse `Informer.X`; while Informer DP hasn't been bound
      // yet (Stage 6.2b — XAML is loaded before the parent DataContext
      // pushes the per-tile informer) both values arrive as
      // DependencyProperty.UnsetValue. Cast-throwing here would crash the
      // first load.
      if (values == null || values.Length < 2 ||
          values[0] is not WeatherType wt ||
          values[1] is not WeatherPeriod wp)
        return null;
      int n = 0;
      if (wp == WeatherPeriod.DayAfterTomorrowEvening || wp == WeatherPeriod.DayAfterTomorrowNight ||
          wp == WeatherPeriod.TomorrowEvening || wp == WeatherPeriod.TomorrowNight ||
          wp == WeatherPeriod.TodayEvening || wp == WeatherPeriod.TodayNight ||
         (wp == WeatherPeriod.Now && (DateTime.Now.Hour >= 18 || DateTime.Now.Hour < 6)))
        n = 1;

      return Application.Current.TryFindResource(WeatherFormatter.weather_types_to_picture[wt][n]) as Canvas;
    }

    public object[] ConvertBack(object value, Type[] targetType, object parameter, CultureInfo culture)
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
      // Same UnsetValue / pre-bind race as WeatherToPicture above.
      if (value is not WindDirection wd)
        return null;

      return Application.Current.TryFindResource(WeatherFormatter.wind_direction_to_picture[wd]) as Canvas;
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
      // Pre-bind UnsetValue is treated as "no data" → opacity 0, same as the
      // false branch. Avoids the (bool) cast throw before Informer arrives.
      if (value is not bool ws)
        return 0;
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

  public class WeatherStatusToVisibility : MarkupExtension, IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      // Pre-bind UnsetValue → Collapsed, same as the false branch. Otherwise
      // the cast throws on the very first load before the parent DataContext
      // pushes a WeatherInformer into Weather.Informer.
      if (value is not bool ws)
        return Visibility.Collapsed;
      return ws ? Visibility.Visible : Visibility.Collapsed;
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

  /// <summary>
  /// View-model layer over <see cref="IWeatherSnapshotStore"/>: maps the
  /// currently selected <see cref="WeatherPeriod"/> to the right slice
  /// of the cached snapshot (Now → current; future periods → forecast)
  /// and surfaces those values as INotifyPropertyChanged properties for
  /// XAML binding. Stage 5 keeps the property layout from the legacy
  /// class so Weather.xaml binding paths don't need to change.
  /// </summary>
  public class WeatherInformer : INotifyPropertyChanged
  {
    private readonly IWeatherSnapshotStore _store;
    private readonly DispatcherTimer _weatherTick = new DispatcherTimer();

    private string _dbg_info = "";
    private bool _weather_status_temperature = false;
    private bool _weather_status_weather = false;
    private bool _weather_status_wind = false;
    private bool _weather_status_pressure = false;
    private bool _weather_status_humidity = false;

    private double _temperature = 0.0, _temperature_low, _temperature_high;
    private double _wind_speed = 0.0;
    private WindDirection _wind_direction = WindDirection.Undefined;
    private double _humidity = 0.0;
    private WeatherType _weather_type = WeatherType.Undefined;
    private double _pressure = 0.0;

    private WeatherPeriod _weather_period = WeatherPeriod.Undefined;
    private bool _closed;

    public WeatherInformer(IWeatherSnapshotStore store)
    {
      _store = store;
      _weatherTick.Tick += weather_Tick;
      _weatherTick.Interval = TimeSpan.FromSeconds(60.0);
      _weatherTick.Start();

      // Push updates as soon as the polling service writes a new snapshot
      // rather than waiting up to a minute for the next dispatcher tick.
      _store.Updated += OnSnapshotUpdated;
    }

    public string Temperature
    {
      get { return (_temperature >= 0 ? "+" : "") + _temperature.ToString(); }
      set { _temperature = double.Parse(value, CultureInfo.InvariantCulture); RaisePropertyChanged("Temperature"); }
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
        _temperature_low = double.Parse(temps[0], CultureInfo.InvariantCulture);
        _temperature_high = double.Parse(temps[1], CultureInfo.InvariantCulture);
        RaisePropertyChanged("TemperatureRange");
      }
    }

    public double Pressure { get { return _pressure; } set { _pressure = value; RaisePropertyChanged("Pressure"); } }
    public double Humidity { get { return _humidity; } set { _humidity = value; RaisePropertyChanged("Humidity"); } }
    public double WindSpeed { get { return _wind_speed; } set { _wind_speed = value; RaisePropertyChanged("WindSpeed"); } }
    public WindDirection WindDirection { get { return _wind_direction; } set { _wind_direction = value; RaisePropertyChanged("WindDirection"); } }

    public WeatherType Weather { get { return _weather_type; } set { _weather_type = value; RaisePropertyChanged("Weather"); } }

    public WeatherPeriod Weather_Period
    {
      get { return _weather_period; }
      set
      {
        _weather_period = value;
        Refresh();
        RaisePropertyChanged("Weather_Period");
      }
    }
    public bool Weather_Status_Temperature { get { return _weather_status_temperature; } set { _weather_status_temperature = value; RaisePropertyChanged("Weather_Status_Temperature"); } }
    public bool Weather_Status_Weather { get { return _weather_status_weather; } set { _weather_status_weather = value; RaisePropertyChanged("Weather_Status_Weather"); } }
    public bool Weather_Status_Wind { get { return _weather_status_wind; } set { _weather_status_wind = value; RaisePropertyChanged("Weather_Status_Wind"); } }
    public bool Weather_Status_Pressure { get { return _weather_status_pressure; } set { _weather_status_pressure = value; RaisePropertyChanged("Weather_Status_Pressure"); } }
    public bool Weather_Status_Humidity { get { return _weather_status_humidity; } set { _weather_status_humidity = value; RaisePropertyChanged("Weather_Status_Humidity"); } }

    private void RaisePropertyChanged(string propertyName)
    {
      if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
    }

    private void weather_Tick(object sender, EventArgs e)
    {
      Refresh();
    }

    private void OnSnapshotUpdated()
    {
      // IWeatherSnapshotStore fires from a background thread; marshal to
      // the dispatcher so PropertyChanged subscribers (XAML bindings)
      // see updates on the UI thread.
      if (Application.Current?.Dispatcher is Dispatcher dispatcher)
        dispatcher.BeginInvoke(new Action(Refresh));
      else
        Refresh();
    }

    private void Refresh()
    {
      if (_weather_period == WeatherPeriod.Now || _weather_period == WeatherPeriod.Undefined)
        ApplyCurrent(_store.Current);
      else
        ApplyForecast(_store.Forecast, _weather_period);
    }

    private void ApplyCurrent(WeatherSnapshot snap)
    {
      if (snap == null)
      {
        ClearAll();
        return;
      }

      if (snap.TemperatureCelsius.HasValue)
      {
        var t = snap.TemperatureCelsius.Value;
        Temperature = t.ToString(CultureInfo.InvariantCulture);
        TemperatureRange = t.ToString(CultureInfo.InvariantCulture) + "|" + t.ToString(CultureInfo.InvariantCulture);
        Weather_Status_Temperature = true;
      }
      else Weather_Status_Temperature = false;

      ApplyShared(snap.WeatherType, snap.WindDirection, snap.WindSpeedMs, snap.Pressure, snap.Humidity);
    }

    private void ApplyForecast(WeatherForecast forecast, WeatherPeriod period)
    {
      if (forecast == null || !forecast.Periods.TryGetValue(period, out var p))
      {
        ClearAll();
        return;
      }

      if (p.Low.HasValue && p.High.HasValue)
      {
        double low = p.Low.Value, high = p.High.Value;
        Temperature = ((low + high) / 2.0).ToString(CultureInfo.InvariantCulture);
        TemperatureRange = low.ToString(CultureInfo.InvariantCulture) + "|" + high.ToString(CultureInfo.InvariantCulture);
        Weather_Status_Temperature = true;
      }
      else Weather_Status_Temperature = false;

      ApplyShared(p.WeatherType, p.WindDirection, p.WindSpeedMs, p.Pressure, p.Humidity);
    }

    private void ApplyShared(WeatherType wt, WindDirection wd, double? wind, double? pressure, double? humidity)
    {
      if (wt != WeatherType.Undefined)
      {
        Weather = wt;
        Weather_Status_Weather = true;
      }
      else Weather_Status_Weather = false;

      if (wind.HasValue)
      {
        WindDirection = wd;
        WindSpeed = wind.Value;
        Weather_Status_Wind = true;
      }
      else Weather_Status_Wind = false;

      if (pressure.HasValue)
      {
        Pressure = pressure.Value;
        Weather_Status_Pressure = true;
      }
      else Weather_Status_Pressure = false;

      if (humidity.HasValue)
      {
        Humidity = humidity.Value;
        Weather_Status_Humidity = true;
      }
      else Weather_Status_Humidity = false;
    }

    private void ClearAll()
    {
      Weather_Status_Temperature = false;
      Weather_Status_Weather = false;
      Weather_Status_Wind = false;
      Weather_Status_Pressure = false;
      Weather_Status_Humidity = false;
    }

    public void Close()
    {
      if (_closed)
        return;
      _closed = true;

      // DispatcherTimer keeps a strong reference to weather_Tick and
      // therefore to this WeatherInformer. Without Stop + unsubscribe
      // the timer would survive window teardown.
      _weatherTick.Stop();
      _weatherTick.Tick -= weather_Tick;

      _store.Updated -= OnSnapshotUpdated;
    }

    // INotifyPropertyChanged
    public event PropertyChangedEventHandler PropertyChanged;
  }
}
