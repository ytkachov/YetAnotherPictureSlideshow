using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using System.Windows.Markup;
using System.Globalization;

using informers;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace presenters
{
  public class gestureformatter
  {
    static public Dictionary<Fingers, string[]> gesture_types_to_picture = new Dictionary<Fingers, string[]>()
    {
      { Fingers.None,                  new string [] { "hand_right_hollow", "" } },
      { Fingers.RightFive,             new string [] { "hand_right_5", " " } }
    };


  }

  public class LeapMotionToPicture : MarkupExtension, IMultiValueConverter
  {
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
      if (values == null)
        return null;

      return Application.Current.TryFindResource(gestureformatter.gesture_types_to_picture[Fingers.RightFive][0]) as Canvas;
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

  /// <summary>
  /// Interaction logic for LeapInformer.xaml
  /// </summary>
  public partial class LeapMotionPresenter : UserControl, INotifyPropertyChanged
  {
    private LeapMotionInformer _lm_informer;
    private DispatcherTimer _handShake;

    public LeapMotionPresenter()
    {
      InitializeComponent();
      (Content as FrameworkElement).DataContext = this;

      BrushConverter bc = new BrushConverter();
      HandFillColor = (Brush)bc.ConvertFrom("#89A02C");

      LeapMotion = new LeapMotionInformer();
      _handShake = new DispatcherTimer();
      _handShake.Interval = TimeSpan.FromSeconds(5);
      _handShake.Tick += new EventHandler(handshake_Tick);
      _handShake.Start();
    }

    void handshake_Tick(object sender, EventArgs e)
    {
      RotateTransform rt = new RotateTransform();
      rt.CenterX = 50.0;
      rt.CenterY = 100.0;

      TranslateTransform tt = new TranslateTransform();
      DoubleAnimation da = new DoubleAnimation(-10, 10.0, new Duration(TimeSpan.FromMilliseconds(50)));
      da.RepeatBehavior = new RepeatBehavior(5);
      da.AutoReverse = true;

      tt.BeginAnimation(TranslateTransform.XProperty, da);
      rt.BeginAnimation(RotateTransform.AngleProperty, da);

      L_content.RenderTransform = tt;
    }

    public static readonly DependencyProperty HandStrokeColorProperty = DependencyProperty.Register("HandStrokeColor", typeof(Brush), typeof(LeapMotionPresenter), new UIPropertyMetadata(Brushes.White));
    public static readonly DependencyProperty HandFillColorProperty = DependencyProperty.Register("HandFillColor", typeof(Brush), typeof(LeapMotionPresenter), new UIPropertyMetadata(Brushes.Black));
    public static readonly DependencyProperty HandFillOpacityProperty = DependencyProperty.Register("HandFillOpacity", typeof(double), typeof(LeapMotionPresenter), new UIPropertyMetadata(0.7));

    public LeapMotionInformer LeapMotion
    {
      get { return _lm_informer; }
      set
      {
        _lm_informer = value;
      }

    }

    public Brush HandFillColor
    {
      get { return (Brush)GetValue(HandFillColorProperty); }
      set { SetValueDP(HandFillColorProperty, value); }
    }

    public Brush HandStrokeColor
    {
      get { return (Brush)GetValue(HandStrokeColorProperty); }
      set { SetValueDP(HandStrokeColorProperty, value); }
    }

    public double HandFillOpacity
    {
      get { return (double)GetValue(HandFillOpacityProperty); }
      set { SetValueDP(HandFillOpacityProperty, value); }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    private void SetValueDP(DependencyProperty dp, object value, [System.Runtime.CompilerServices.CallerMemberName] string caller_name = null)
    {
      SetValue(dp, value);
      if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs(caller_name));
    }
  }
}

