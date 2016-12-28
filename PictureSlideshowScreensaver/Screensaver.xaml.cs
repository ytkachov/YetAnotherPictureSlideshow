using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

using BatteryMonitor;
using WindowsInput;

using weather;
using presenters;

namespace PictureSlideshowScreensaver
{


  class Settings
  {
    public string _path = null;
    public double _updateInterval = 5; // seconds
    public int _fadeSpeed = 200;       // milliseconds
    public int _startOffset = 0;
    public bool _writeStat = false;
    public string _writeStatPath;
    public bool _dependOnBattery = false;
    public bool _workAtNight = true;

    public Settings()
    {
      RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\PictureSlideshowScreensaver");
      if (key != null)
      {
        _path = (string)key.GetValue("ImageFolder");
        _updateInterval = double.Parse((string)key.GetValue("Interval") ?? "10.0");
        _fadeSpeed = int.Parse((string)key.GetValue("FadeTime") ?? "1000");
        _writeStat = int.Parse((string)key.GetValue("WriteStat") ?? "0") == 1;
        _writeStatPath = (string)key.GetValue("WriteStatFolder");
        _dependOnBattery = int.Parse((string)key.GetValue("DependOnBattery") ?? "0") == 1;
      }
    }

}



public interface ILeapEventDelegate
{
  void LeapEventNotification(string EventName);
}

public class LeapEventListener : Leap.Listener
  {
  ILeapEventDelegate eventDelegate;

  public LeapEventListener(ILeapEventDelegate delegateObject)
  {
    this.eventDelegate = delegateObject;
  }

  public override void OnInit(Leap.Controller controller)
  {
    this.eventDelegate.LeapEventNotification("onInit");
  }
  public override void OnConnect(Leap.Controller controller)
  {
    controller.SetPolicy(Leap.Controller.PolicyFlag.POLICY_IMAGES);
    controller.EnableGesture(Leap.Gesture.GestureType.TYPE_SWIPE);
    this.eventDelegate.LeapEventNotification("onConnect");
  }

  public override void OnFrame(Leap.Controller controller)
  {
    this.eventDelegate.LeapEventNotification("onFrame");
  }
  public override void OnExit(Leap.Controller controller)
  {
    this.eventDelegate.LeapEventNotification("onExit");
  }
  public override void OnDisconnect(Leap.Controller controller)
  {
    this.eventDelegate.LeapEventNotification("onDisconnect");
  }
}

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class Screensaver : Window, ILeapEventDelegate
  {
    private Random _rand;
    private Settings _settings = new Settings();
    private InputSimulator _input = new InputSimulator();

    private ImagesProvider _images = new LocalImages();
    private DispatcherTimer _switchImage;
    private DispatcherTimer _changeWeatherForecast;
    private Point _mouseLocation = new Point(0, 0);

    private System.Drawing.Rectangle _bounds;
    private cPower _power;
    private int _prevTime = 0;

    bool _isClosing = false;
    private Leap.Controller _controller = new Leap.Controller();
    private LeapEventListener _listener;

    public Screensaver(System.Drawing.Rectangle bounds, int offset)
    {

      InitializeComponent();

      _listener = new LeapEventListener(this);
      _controller.AddListener(_listener);

      _bounds = bounds;

      _images.init(new string[] { _settings._path, _settings._writeStat ? _settings._writeStatPath : "" });

      _switchImage = new DispatcherTimer();
      _switchImage.Interval = TimeSpan.FromSeconds(_settings._updateInterval);
      _switchImage.Tick += new EventHandler(fade_Tick);

      _changeWeatherForecast = new DispatcherTimer();
      _changeWeatherForecast.Interval = TimeSpan.FromMinutes(1);
      _changeWeatherForecast.Tick += new EventHandler(forecast_Tick);
      forecast_Tick(null, null);

      _settings._startOffset = offset;
      _rand = new Random(DateTime.Now.Second);
      _power = new cPower();
      _power.BatteryUpdateEvery = 30;
    }

    delegate void LeapEventDelegate(string EventName);
    public void LeapEventNotification(string EventName)
    {
      if (this.CheckAccess())
      {
        switch (EventName)
        {
          case "onInit":
            break;

          case "onConnect":
            connectHandler();
            break;

          case "onFrame":
            if (!_isClosing)
              newFrameHandler(_controller.Frame());

            break;
        }
      }
      else
      {
        Dispatcher.Invoke(new LeapEventDelegate(LeapEventNotification), new object[] { EventName });
      }
    }

    void connectHandler()
    {
      _controller.SetPolicy(Leap.Controller.PolicyFlag.POLICY_DEFAULT);
      _controller.EnableGesture(Leap.Gesture.GestureType.TYPE_SWIPE);
      _controller.Config.SetFloat("Gesture.Swipe.MinLength", 100.0f);
    }

    void newFrameHandler(Leap.Frame frame)
    {
      string stat = frame.Id.ToString() + " TM:" +
                    frame.Timestamp.ToString() + " FPS:" +
                    frame.CurrentFramesPerSecond.ToString() + " VLD:" +
                    frame.IsValid.ToString() + " GC:" +
                    frame.Gestures().Count.ToString();

      PhotoProperties.Photo_Description = stat;
    }

    public Screensaver(System.Drawing.Rectangle bounds)
      : this(bounds, 0)
    {

    }

    void fade_Tick(object sender, EventArgs e)
    {
      if (!_settings._workAtNight && DateTime.Now.Hour < 7)
        return;        // фотографии не меняются ночью.

      NextImage();
    }

    void forecast_Tick(object sender, EventArgs e)
    {
      WeatherPeriod[] wp = new WeatherPeriod[] 
      {
        WeatherPeriod.TodayMorning, WeatherPeriod.TodayDay, WeatherPeriod.TodayEvening, WeatherPeriod.TodayNight,
        WeatherPeriod.TomorrowMorning, WeatherPeriod.TomorrowDay, WeatherPeriod.TomorrowEvening
      };

      DateTime dt = DateTime.Now;
      int wpidx;
      if (dt.Hour < 11)
        wpidx = 0;
      else if (dt.Hour < 17)
        wpidx = 1;
      else if (dt.Hour < 23)
        wpidx = 2;
      else
        wpidx = 3;

      for (int i = 0; i < 3; i++)
      {
        WeatherPresenter w = FindName("W_N" + i) as WeatherPresenter;

        if (w != null && w.WeatherPeriod != wp[wpidx + i])
          w.WeatherPeriod = wp[wpidx + i];
      }

    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
      // Maximize window
      //this.WindowState = System.Windows.WindowState.Maximized;
#if DEBUG
      this.Topmost = false;
      this.WindowStyle = WindowStyle.ThreeDBorderWindow;
      this.ResizeMode = ResizeMode.CanResizeWithGrip;
#endif

      _switchImage.Start();
    }

    private void NextImage()
    {
      // move mouse to prevent sleeping
      _input.Mouse.MoveMouseBy(_rand.Next(-1, 2), _rand.Next(-1, 2));

      // write stat every day at 8PM
      if (_settings._writeStat && _prevTime == 20 && DateTime.Now.Hour == _prevTime + 1)
        _images.WriteStat(_settings._writeStatPath);

      _prevTime = DateTime.Now.Hour;

      ImageInfo nextphoto = _images.GetNext();
      if (nextphoto != null)
      {
        try
        {
          PhotoProperties.Photo_Description = nextphoto.description;
          if (img1.Opacity == 0)
          {
            SetImage(img1, nextphoto);

            img1.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(_settings._fadeSpeed)));
            img2.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(_settings._fadeSpeed)));
          }
          else if (img2.Opacity == 0)
          {
            SetImage(img2, nextphoto);

            img1.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(_settings._fadeSpeed)));
            img2.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(_settings._fadeSpeed)));
          }
          return;
        }
        catch (Exception ex)
        {
          Console.WriteLine("ERROR: " + ex.Message);
          return;
        }
      }
    }

    //private int nxtimg = 0;
    private void SetImage(Image img, ImageInfo nextphoto)
    {
      if (_settings._dependOnBattery && _power.HasBattery && _power.BatteryLifePercent < 10)
        return;

      BitmapImage bmp_img = nextphoto.bitmap;

      img.Stretch = Stretch.Uniform;
      if (bmp_img.Width > bmp_img.Height * 1.2)
        img.Stretch = Stretch.UniformToFill;

      img.Source = bmp_img;
      if (img.ActualHeight > 0 && img.ActualWidth > 0)
      {
        // if image is valid
        if (!_settings._dependOnBattery ||
            !_power.HasBattery ||
            (_power.HasBattery && _power.BatteryLifePercent > 20))
        {
          double cx = img.ActualWidth / 2;
          double cy = img.ActualHeight / 2;
          double cs = 1.0 + 0.1 * _rand.NextDouble();

          PhotoProperties.Set_Faces_Found = nextphoto.accent_count;
          if (nextphoto.accent_count != 0)
          {
            double dc = 1;
            dc = bmp_img.PixelHeight / img.ActualHeight;
            var accent = nextphoto.accent;

            cx += accent.X / dc;
            cy += accent.Y / dc;
            cs = 1.05 + 0.4 * _rand.NextDouble(); 
          }

          ScaleTransform st = new ScaleTransform(1.0, 1.0, cx, cy);
          DoubleAnimation da = new DoubleAnimation(cs, new Duration(TimeSpan.FromSeconds(_settings._updateInterval)));

          st.BeginAnimation(ScaleTransform.ScaleXProperty, da);
          st.BeginAnimation(ScaleTransform.ScaleYProperty, da);

          img.RenderTransform = st;
        }
      }
    }

    private void Shutdown()
    {
      _isClosing = true;
      _controller.RemoveListener(_listener);
      _controller.Dispose();

      if (_settings._writeStat)
        _images.WriteStat(_settings._writeStatPath);

      Application.Current.Shutdown();
    }

    private void bExit_Click(object sender, RoutedEventArgs e)
    {
      Shutdown();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.Key == Key.Escape)
        Shutdown();            
      else if (e.Key == Key.F)
      {
        // show weather forecast
        WeatherForecast.Visibility = (WeatherForecast.Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible);
      }
    }
  }

}
