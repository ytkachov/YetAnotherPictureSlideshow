using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using weather;
using informers;
using System.Windows.Markup;
using System.Globalization;

namespace presenters
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

  public class WeatherToPicture : MarkupExtension, IMultiValueConverter
  {
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
      if (values == null)
        return null;

      WeatherType wt = (WeatherType)values[0];
      WeatherPeriod wp = (WeatherPeriod)values[1];
      int n = 0;
      if (wp == WeatherPeriod.DayAfterTomorrowEvening || wp == WeatherPeriod.DayAfterTomorrowNight ||
          wp == WeatherPeriod.TomorrowEvening || wp == WeatherPeriod.TomorrowNight ||
          wp == WeatherPeriod.TodayEvening || wp == WeatherPeriod.TodayNight ||
         (wp == WeatherPeriod.Now && (DateTime.Now.Hour >= 18 || DateTime.Now.Hour < 6)))
        n = 1;

      return Application.Current.TryFindResource(weatherformatter.weather_types_to_picture[wt][n]) as Canvas;
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

  public class ShowRange : MarkupExtension, IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      ShowWhat ws = (ShowWhat)value;
      return (ws == ShowWhat.TemperatureRange ? Visibility.Visible : Visibility.Collapsed);
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

  public class ShowValue : MarkupExtension, IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      ShowWhat ws = (ShowWhat)value;
      return (ws == ShowWhat.TemperatureValue ? Visibility.Visible : Visibility.Collapsed);
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


  public enum ShowWhat
  {
    TemperatureRange,
    TemperatureValue
  }

  /// <summary>
  /// Interaction logic for WeatherPresenter.xaml
  /// </summary>
  public partial class WeatherPresenter : UserControl, INotifyPropertyChanged
  {
    private WeatherInformer _weatherInformer;

    public WeatherPresenter()
    {
      Dispatcher.ShutdownStarted += OnShutdownStarted;

      _weatherInformer = new WeatherInformer();

      InitializeComponent();
      (Content as FrameworkElement).DataContext = this;
      Weather.Weather_Period = WeatherPeriod;
      BorderColor = Brushes.White;
      FillColor = Brushes.White;
      StrokeColor = Brushes.Black;
      FontSize = 30;
      FontFamily = new FontFamily("Segoe UI Light");

    }

    private void OnShutdownStarted(object sender, EventArgs e)
    {
      _weatherInformer.Close();
    }

    public static readonly DependencyProperty ShowProperty = DependencyProperty.Register("Show", typeof(ShowWhat), typeof(WeatherPresenter), new UIPropertyMetadata(ShowWhat.TemperatureRange));
    public static readonly DependencyProperty WeatherPeriodProperty = DependencyProperty.Register("WeatherPeriod", typeof(WeatherPeriod), typeof(WeatherPresenter), new FrameworkPropertyMetadata(OnWeatherPeriodUpdated));
    public static readonly DependencyProperty PictureSizeProperty = DependencyProperty.Register("PictureSize", typeof(double), typeof(WeatherPresenter), new UIPropertyMetadata(40.0));
    public static readonly DependencyProperty ChildMarginProperty = DependencyProperty.Register("ChildMargin", typeof(double), typeof(WeatherPresenter), new UIPropertyMetadata(2.0));
    public static readonly DependencyProperty ChildBorderThicknessProperty = DependencyProperty.Register("ChildBorderThickness", typeof(double), typeof(WeatherPresenter), new UIPropertyMetadata(2.0));
    public static readonly DependencyProperty ChildrenWidthsProperty = DependencyProperty.Register("ChildrenWidths", typeof(string), typeof(WeatherPresenter), new FrameworkPropertyMetadata(OnChildrenWidthUpdated));
    public static readonly DependencyProperty ChildPaddingProperty = DependencyProperty.Register("ChildPadding", typeof(double), typeof(WeatherPresenter), new UIPropertyMetadata(3.0));
    public static readonly DependencyProperty FillColorProperty = DependencyProperty.Register("FillColor", typeof(Brush), typeof(WeatherPresenter), new UIPropertyMetadata(Brushes.White));
    public static readonly DependencyProperty StrokeColorProperty = DependencyProperty.Register("StrokeColor", typeof(Brush), typeof(WeatherPresenter), new UIPropertyMetadata(Brushes.Black));
    public static readonly DependencyProperty BorderColorProperty = DependencyProperty.Register("BorderColor", typeof(Brush), typeof(WeatherPresenter), null);

    public WeatherPeriod WeatherPeriod
    {
      get { return (WeatherPeriod)GetValue(WeatherPeriodProperty); }
      set
      {
        Weather.Weather_Period = value;
        SetValueDP(WeatherPeriodProperty, value);
      }
    }

    public ShowWhat Show
    {
      get { return (ShowWhat)GetValue(ShowProperty); }
      set { SetValueDP(ShowProperty, value); }
    }

    public double PictureSize
    {
      get { return (double)GetValue(PictureSizeProperty); }
      set { SetValueDP(PictureSizeProperty, value); }
    }

    public double ChildMargin
    {
      get { return (double)GetValue(ChildMarginProperty); }
      set { SetValueDP(ChildMarginProperty, value); }
    }

    public string ChildrenWidths
    {
      get { return (string)GetValue(ChildrenWidthsProperty); }
      set { SetValueDP(ChildrenWidthsProperty, value); }
    }

    public double ChildBorderThickness
    {
      get { return (double)GetValue(ChildBorderThicknessProperty); }
      set { SetValueDP(ChildBorderThicknessProperty, value); }
    }

    public double ChildPadding
    {
      get { return (double)GetValue(ChildPaddingProperty); }
      set { SetValueDP(ChildPaddingProperty, value); }
    }

    public Brush FillColor
    {                                                                                                            
      get { return (Brush)GetValue(FillColorProperty); }
      set { SetValueDP(FillColorProperty, value); }
    }

    public Brush StrokeColor
    {
      get { return (Brush)GetValue(StrokeColorProperty); }
      set { SetValueDP(StrokeColorProperty, value); }
    }

    public Brush BorderColor
    {
      get { return (Brush)GetValue(BorderColorProperty); }
      set { SetValueDP(BorderColorProperty, value); }
    }

    public WeatherInformer Weather
    {
      get { return _weatherInformer; }
      set
      {
        _weatherInformer = value;
      }
    }

    // INotifyPropertyChanged
    public event PropertyChangedEventHandler PropertyChanged;
    private void SetValueDP(DependencyProperty dp, object value, [System.Runtime.CompilerServices.CallerMemberName] string caller_name = null)
    {
      SetValue(dp, value);
      if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs(caller_name));
    }

    private static void OnWeatherPeriodUpdated(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
      var wp = (WeatherPresenter)dependencyObject;
      wp.Weather.Weather_Period = wp.WeatherPeriod;
    }

    private static void OnChildrenWidthUpdated(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
      var wp = (WeatherPresenter)dependencyObject;
      string cw = wp.ChildrenWidths;

      var widths = cw.Split(',');
      for (int i = 0; i < widths.Length; i++)
      {
        double w = 0;
        if (double.TryParse(widths[i], out w))
        {

        }
      }
    }
  }
}
