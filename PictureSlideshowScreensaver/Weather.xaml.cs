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
  /// Interaction logic for Weather.xaml
  /// </summary>
  public partial class Weather : UserControl, INotifyPropertyChanged
  {
    private WeatherInformer _weatherInformer;
    private string _componentWidths;

    public Weather()
    {
      Dispatcher.ShutdownStarted += OnShutdownStarted;

      _weatherInformer = new WeatherInformer();
      InitializeComponent();
      (Content as FrameworkElement).DataContext = this;
      WeatherInfo.Weather_Period = WeatherPeriod;
      BorderColor = Brushes.White;
      FillColor = Brushes.White;
      StrokeColor = Brushes.Black;
      FontSize = 30;
      FontFamily = new FontFamily("Segoe UI Light");

 //     H_Border.ClearValue(Border.WidthProperty);
    }

    private void OnShutdownStarted(object sender, EventArgs e)
    {
      _weatherInformer.Close();
    }

    public static readonly DependencyProperty ShowProperty = DependencyProperty.Register("Show", typeof(ShowWhat), typeof(Weather), new UIPropertyMetadata(ShowWhat.TemperatureRange));
    public static readonly DependencyProperty WeatherPeriodProperty = DependencyProperty.Register("WeatherPeriod", typeof(WeatherPeriod), typeof(Weather), new FrameworkPropertyMetadata(OnWeatherPeriodUpdated));
    public static readonly DependencyProperty PictureSizeProperty = DependencyProperty.Register("PictureSize", typeof(double), typeof(Weather), new UIPropertyMetadata(40.0));
    public static readonly DependencyProperty ChildMarginProperty = DependencyProperty.Register("ChildMargin", typeof(double), typeof(Weather), new UIPropertyMetadata(2.0));
    public static readonly DependencyProperty ChildBorderThicknessProperty = DependencyProperty.Register("ChildBorderThickness", typeof(double), typeof(Weather), new UIPropertyMetadata(2.0));
    public static readonly DependencyProperty ChildrenWidthsProperty = DependencyProperty.Register("ChildrenWidths", typeof(string), typeof(Weather), new FrameworkPropertyMetadata(OnChildrenWidthUpdated));
    public static readonly DependencyProperty ChildPaddingProperty = DependencyProperty.Register("ChildPadding", typeof(double), typeof(Weather), new UIPropertyMetadata(3.0));
    public static readonly DependencyProperty FillColorProperty = DependencyProperty.Register("FillColor", typeof(Brush), typeof(Weather), new UIPropertyMetadata(Brushes.White));
    public static readonly DependencyProperty StrokeColorProperty = DependencyProperty.Register("StrokeColor", typeof(Brush), typeof(Weather), new UIPropertyMetadata(Brushes.Black));
    public static readonly DependencyProperty BorderColorProperty = DependencyProperty.Register("BorderColor", typeof(Brush), typeof(Weather), null);

    public WeatherPeriod WeatherPeriod
    {
      get { return (WeatherPeriod)GetValue(WeatherPeriodProperty); }
      set
      {
        WeatherInfo.Weather_Period = value;
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

    public WeatherInformer WeatherInfo
    {
      get { return _weatherInformer; }
      set
      {
        _weatherInformer = value;
      }
    }

    public string  ComponentWidths
    {
      get
      {
        return _componentWidths;
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
      var wp = (Weather)dependencyObject;
      wp.WeatherInfo.Weather_Period = wp.WeatherPeriod;
    }

    private static void OnChildrenWidthUpdated(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
      var wp = (Weather)dependencyObject;
      string cw = wp.ChildrenWidths;

      FrameworkElement[] children = { wp.T_Border, wp.W_Border, wp.WIND_Border, wp.P_Border, wp.H_Border };
      var widths = cw.Split(',');
      for (int i = 0; i < widths.Length; i++)
      {
        if (i >= children.Length)
          break;

        double w = 0;
        if (widths[i].Length != 0 && double.TryParse(widths[i], out w))
          children[i].Width = w;
        else
          children[i].ClearValue(WidthProperty);
      }
    }

    private void OnLayoutUpdated(object sender, EventArgs e)
    {
      string cw = "";
      FrameworkElement[] children = { T_Border, W_Border, WIND_Border, P_Border, H_Border };
      for (int i = 0; i < children.Length; i++)
        cw += (cw.Length == 0 ? "" : ",") + children[i].ActualWidth.ToString();

      _componentWidths = cw;
    }
  }
}
