using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.IO;
using Microsoft.Win32;
using System.Threading;
using System.Windows.Interop;

using Emgu.CV;
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
          PhotoProperties.Date_Photo_Taken = nextphoto._dateTaken == null ? "" : nextphoto._dateTaken.Value.ToString("dd/MM/yyyy");
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

      BitmapImage bmp_img = new BitmapImage(new Uri(nextphoto._name));

      System.Drawing.Bitmap bitmap;
      using (MemoryStream outStream = new MemoryStream())
      {
        BitmapEncoder enc = new BmpBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp_img));
        enc.Save(outStream);
        bitmap = new System.Drawing.Bitmap(outStream);

        System.Drawing.RotateFlipType rf = System.Drawing.RotateFlipType.RotateNoneFlipNone;
        switch (nextphoto._orientation)
        {
          case 1:
            break;

          case 2:
            rf = System.Drawing.RotateFlipType.RotateNoneFlipX;
            break;

          case 3:
            rf = System.Drawing.RotateFlipType.Rotate180FlipNone;
            break;
          case 4:
            rf = System.Drawing.RotateFlipType.RotateNoneFlipY;
            break;
          case 5:
            rf = System.Drawing.RotateFlipType.Rotate90FlipX;
            break;
          case 6:
            rf = System.Drawing.RotateFlipType.Rotate90FlipNone;
            break;
          case 7:
            rf = System.Drawing.RotateFlipType.Rotate270FlipX;
            break;
          case 8:
            rf = System.Drawing.RotateFlipType.Rotate270FlipNone;
            break;
        }

        if (rf != System.Drawing.RotateFlipType.RotateNoneFlipNone)
        {
          bitmap.RotateFlip(rf);
          bmp_img = Bitmap2BitmapImage(bitmap);
        }
      }

      if (!nextphoto._processed)
      {

        if (!_settings._dependOnBattery || 
            !_power.HasBattery || 
            (_power.HasBattery && _power.BatteryLifePercent > 30))
        {
          nextphoto._processed = true;
          nextphoto._dmult = 3.0;
          List<System.Drawing.Rectangle> faces = new List<System.Drawing.Rectangle>();

          System.Drawing.Bitmap b = new System.Drawing.Bitmap((int)(bitmap.Width / nextphoto._dmult), (int)(bitmap.Height / nextphoto._dmult), System.Drawing.Imaging.PixelFormat.Format24bppRgb);
          using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage((System.Drawing.Image)b))
          {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(bitmap, 0, 0, b.Width, b.Height);

            try
            {
              Image<Emgu.CV.Structure.Bgr, byte> cvimg = new Image<Emgu.CV.Structure.Bgr, byte>(b);
              Mat cvmat = new Mat(cvimg.Mat, new System.Drawing.Rectangle(new System.Drawing.Point(0, 0), cvimg.Size));

              long detectionTime;
              FaceDetection.DetectFace.Detect(cvmat, "haarcascade_frontalface_alt2.xml", faces, out detectionTime);

              if (faces.Count != 0)
                nextphoto._faces = faces;

            }
            catch (Exception e)
            {
              string err = e.Message;
            }
            //if (faces.Count != 0)
            //{
            //  System.Drawing.Pen rpen = new System.Drawing.Pen(System.Drawing.Brushes.Red, (float)3.0);
            //  foreach (var face in faces)
            //    g.DrawRectangle(rpen, face);

            //  b.Save(String.Format(@"C:\Temp\{0}.{1}.{2}.png", nxtimg++, faces.Count, detectionTime), ImageFormat.Png);
            //}
          }
        }
      }

      img.Stretch = Stretch.Uniform;
      if (bmp_img.Width > bmp_img.Height * 1.2)
        img.Stretch = Stretch.UniformToFill;

      img.Source = bmp_img;
      if (img.ActualHeight > 0 && img.ActualWidth > 0)
      {
        double cx = img.ActualWidth / 2;
        double cy = img.ActualHeight / 2;

        PhotoProperties.Set_Faces_Found = nextphoto._faces == null ? 0 : nextphoto._faces.Count;
        if (nextphoto._faces != null)
        {
          double dc = 1;
          dc = bmp_img.PixelHeight / img.ActualHeight;

          int fn = _rand.Next(nextphoto._faces.Count);
          double fx = ((nextphoto._faces[fn].Right + nextphoto._faces[fn].Left)   * nextphoto._dmult / 2.0 - bmp_img.PixelWidth / 2.0);
          double fy = ((nextphoto._faces[fn].Top +   nextphoto._faces[fn].Bottom) * nextphoto._dmult / 2.0 - bmp_img.PixelHeight / 2.0);

          cx += fx / dc;
          cy += fy / dc;
        }

        if (!_settings._dependOnBattery ||
            !_power.HasBattery ||
            (_power.HasBattery && _power.BatteryLifePercent > 20))
        {
          ScaleTransform st = new ScaleTransform(1.0, 1.0, cx, cy);
          DoubleAnimation da = new DoubleAnimation((nextphoto._faces == null ? 1.0 : 1.05) +
                                                   (nextphoto._faces == null ? 0.1 : 0.40) * _rand.NextDouble(), new Duration(TimeSpan.FromSeconds(_settings._updateInterval)));

          st.BeginAnimation(ScaleTransform.ScaleXProperty, da);
          st.BeginAnimation(ScaleTransform.ScaleYProperty, da);

          img.RenderTransform = st;
        }
      }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    private BitmapImage Bitmap2BitmapImage(System.Drawing.Bitmap bitmap)
    {
      IntPtr hBitmap = bitmap.GetHbitmap();
      BitmapSource retval;

      try
      {
        retval = Imaging.CreateBitmapSourceFromHBitmap(
                     hBitmap,
                     IntPtr.Zero,
                     Int32Rect.Empty,
                     BitmapSizeOptions.FromEmptyOptions());
      }
      finally
      {
        DeleteObject(hBitmap);
      }

      return (BitmapImage)retval;
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
    }
  }

}
