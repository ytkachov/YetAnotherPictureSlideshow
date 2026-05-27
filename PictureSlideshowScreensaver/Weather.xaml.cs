using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using informers;

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
  /// Interaction logic for Weather.xaml. Stage 6.2b: the per-period
  /// <see cref="WeatherInformer"/> is supplied through the
  /// <see cref="InformerProperty"/> dependency property (typically bound
  /// to <c>ForecastViewModel</c>) — the control no longer reaches into
  /// the DI container for one. Stage 6.4: width sync between tiles now
  /// rides on Grid <c>SharedSizeGroup</c>, so the
  /// <c>ChildrenWidths</c>/<c>ComponentWidths</c>/<c>LayoutUpdated</c>
  /// measurement protocol is gone.
  /// </summary>
  public partial class Weather : UserControl, INotifyPropertyChanged
  {
    public Weather()
    {
      InitializeComponent();
      (Content as FrameworkElement).DataContext = this;
      BorderColor = Brushes.White;
      FillColor = Brushes.White;
      StrokeColor = Brushes.Black;
      FontSize = 30;
      FontFamily = new FontFamily("Segoe UI Light");
    }

    public static readonly DependencyProperty ShowProperty = DependencyProperty.Register("Show", typeof(ShowWhat), typeof(Weather), new UIPropertyMetadata(ShowWhat.TemperatureRange));
    public static readonly DependencyProperty InformerProperty = DependencyProperty.Register("Informer", typeof(WeatherInformer), typeof(Weather), new PropertyMetadata(null));
    public static readonly DependencyProperty ShowSourceProperty = DependencyProperty.Register("ShowSource", typeof(bool), typeof(Weather), new UIPropertyMetadata(false));
    public static readonly DependencyProperty PictureSizeProperty = DependencyProperty.Register("PictureSize", typeof(double), typeof(Weather), new UIPropertyMetadata(40.0));
    public static readonly DependencyProperty ChildMarginProperty = DependencyProperty.Register("ChildMargin", typeof(double), typeof(Weather), new UIPropertyMetadata(2.0));
    public static readonly DependencyProperty ChildBorderThicknessProperty = DependencyProperty.Register("ChildBorderThickness", typeof(double), typeof(Weather), new UIPropertyMetadata(2.0));
    public static readonly DependencyProperty ChildPaddingProperty = DependencyProperty.Register("ChildPadding", typeof(double), typeof(Weather), new UIPropertyMetadata(3.0));
    public static readonly DependencyProperty FillColorProperty = DependencyProperty.Register("FillColor", typeof(Brush), typeof(Weather), new UIPropertyMetadata(Brushes.White));
    public static readonly DependencyProperty StrokeColorProperty = DependencyProperty.Register("StrokeColor", typeof(Brush), typeof(Weather), new UIPropertyMetadata(Brushes.Black));
    public static readonly DependencyProperty BorderColorProperty = DependencyProperty.Register("BorderColor", typeof(Brush), typeof(Weather), null);

    public ShowWhat Show
    {
      get { return (ShowWhat)GetValue(ShowProperty); }
      set { SetValueDP(ShowProperty, value); }
    }

    public WeatherInformer Informer
    {
      get { return (WeatherInformer)GetValue(InformerProperty); }
      set { SetValueDP(InformerProperty, value); }
    }

    /// <summary>Only the live "now" tile sets this to true so the small
    /// source badge appears in its top-right. Forecast tiles share the
    /// UserControl but keep ShowSource at the default false to avoid
    /// 13 chips smeared across the forecast overlay.</summary>
    public bool ShowSource
    {
      get { return (bool)GetValue(ShowSourceProperty); }
      set { SetValueDP(ShowSourceProperty, value); }
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

    // INotifyPropertyChanged
    public event PropertyChangedEventHandler PropertyChanged;
    private void SetValueDP(DependencyProperty dp, object value, [System.Runtime.CompilerServices.CallerMemberName] string caller_name = null)
    {
      SetValue(dp, value);
      if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs(caller_name));
    }
  }
}
