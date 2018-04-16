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
    public bool _noImageFading = false;
    public bool _noImageScaling = false;
    public bool _noImageAccents = false;
    public bool _noNightImageFading = true;
    public bool _noNightImageScaling = true;
    public bool _noNightImageAccents = true;

    enum perfoptions
    {
      work_at_night = 0x0001,
      no_image_fading = 0x0002,
      no_image_scaling = 0x0004,
      no_image_accents = 0x0008,
      no_night_image_fading = 0x0020,
      no_night_image_scaling = 0x0040,
      no_night_image_accents = 0x0080
    }

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

        int dflt = (int)(perfoptions.work_at_night | perfoptions.no_night_image_accents | perfoptions.no_night_image_fading | perfoptions.no_night_image_scaling);
        int po = (int?)key.GetValue("PerformanceOptions") ?? dflt;
        _workAtNight =  (po & (int)perfoptions.work_at_night) != 0;
        _noImageFading = (po & (int)perfoptions.no_image_fading) != 0;
        _noImageScaling = (po & (int)perfoptions.no_image_scaling) != 0;
        _noImageAccents = (po & (int)perfoptions.no_image_accents) != 0;
        _noNightImageFading = (po & (int)perfoptions.no_night_image_fading) != 0;
        _noNightImageScaling = (po & (int)perfoptions.no_night_image_scaling) != 0;
        _noNightImageAccents = (po & (int)perfoptions.no_night_image_accents) != 0;
      }
    }

  }
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class Screensaver : Window
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
    private bool _isNightTime = false;

    public Screensaver(System.Drawing.Rectangle bounds, int offset)
    {

      InitializeComponent();
      _bounds = bounds;

      _images.init(new string[] { _settings._path, _settings._writeStat ? _settings._writeStatPath : "" });

      _switchImage = new DispatcherTimer();
      _switchImage.Interval = TimeSpan.FromSeconds(_settings._updateInterval + offset);
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

    public Screensaver(System.Drawing.Rectangle bounds)
      : this(bounds, 0)
    {

    }

    void fade_Tick(object sender, EventArgs e)
    {
      _switchImage.Interval = TimeSpan.FromSeconds(_settings._updateInterval);

      _isNightTime = DateTime.Now.Hour < 7 || DateTime.Now.Hour >= 23;
      //_isNightTime = true;
      if ((!_settings._workAtNight) && _isNightTime)
        return;        // фотографии не меняются ночью.

      NextImage();
    }

    // это сейчас не актуально - это было для краткого прогноза (который был под текущей погодой)
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
        Weather w = FindName("W_N" + i) as Weather;

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
          var dt = TimeSpan.FromMilliseconds(_settings._noImageFading || 
                                             (_isNightTime && _settings._noNightImageFading) 
                                            ? 0 : _settings._fadeSpeed);

            if (img1.Opacity == 0)
          {
            SetImage(img1, nextphoto);

            img1.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(1, dt));
            img2.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(0, dt));
          }
          else if (img2.Opacity == 0)
          {
            SetImage(img2, nextphoto);

            img1.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(0, dt));
            img2.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(1, dt));
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
      if (bmp_img.PixelWidth > bmp_img.PixelHeight * 1.2)
        img.Stretch = Stretch.UniformToFill;

      img.Source = bmp_img;
      if (img.ActualHeight > 0 && img.ActualWidth > 0)
      {
        // if image is valid
        if (!(_settings._dependOnBattery && _power.HasBattery && _power.BatteryLifePercent < 20) &&
            !(_settings._noImageScaling || (_isNightTime && _settings._noNightImageScaling)))
        {
          double cx = img.ActualWidth / 2;
          double cy = img.ActualHeight / 2;
          double cs = 1.0 + 0.1 * _rand.NextDouble();

          if (!_settings._noImageAccents && !(_isNightTime && _settings._noNightImageAccents))
          {
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
          }

          ScaleTransform st = new ScaleTransform(1.0, 1.0, cx, cy);
          DoubleAnimation da = new DoubleAnimation(cs, new Duration(TimeSpan.FromSeconds(_settings._updateInterval)));

          st.BeginAnimation(ScaleTransform.ScaleXProperty, da);
          st.BeginAnimation(ScaleTransform.ScaleYProperty, da);

          img.RenderTransform = st;
        }
      }

      nextphoto.ReleaseResources();
    }

    private void Shutdown()
    {
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
