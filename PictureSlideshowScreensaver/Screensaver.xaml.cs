using System;
using System.Collections.Generic;
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
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Threading;
using ExifLib;
using CustomControls;
using weather;
using presenters;
using System.Drawing.Imaging;
using Emgu.CV;
using Emgu.CV.CvEnum;
using System.Windows.Interop;
using BatteryMonitor;

namespace PictureSlideshowScreensaver
{
  class PhotoInfo
  {
    public PhotoInfo(string nm) { _name = nm; }
    public string _name;
    public DateTime ? _dateTaken;
    public int _shown = 0;
    public UInt16 _orientation = 0;
    public List<System.Drawing.Rectangle> _faces = null;
    public bool _processed = false;
    internal double _dmult;
  }

  class ImagesInfo
  {
    private int _currentSecCount;
    private IEnumerator<int> _currentSecEnum;
    private DateTime[] _dates;
    private PhotoInfo _currentImage;

    private PhotoInfo[] _images;
    private Dictionary<DateTime, List<int>> _imagesByDate;
    private List<PhotoInfo> _imagesTmp = new List<PhotoInfo>();
    private Random _rand;
    private int _maxSecNumber = 10;
    private int _maxSecLength = 30;
    private int _shownImages = 0;

    public void Add(string name)
    {
      PhotoInfo ii = new PhotoInfo(name);

      try
      {
        using (var reader = new ExifReader(name))
        {
          DateTime datePictureTaken;
          if (reader.GetTagValue(ExifTags.DateTimeOriginal, out datePictureTaken))
            ii._dateTaken = datePictureTaken;

          UInt16 orientation;
          if (reader.GetTagValue(ExifTags.Orientation, out orientation))
            ii._orientation = orientation;
        }
      }
      catch (Exception ex)
      {
        ex.ToString();
      }

      _imagesTmp.Add(ii);
    }

    public int Count { get { return _images == null ? _imagesTmp.Count : _images.Length; } }
    public int Shown { get { return _shownImages; } }

    public PhotoInfo MoveNext()
    {
      if (_images == null)
        buildImageSec();

      if (_currentSecCount <= 0)
      {
        DateTime currentSecDate = _dates[_rand.Next(0, _dates.Length)];

        _imagesByDate[currentSecDate] = RandomizeGenericList(_imagesByDate[currentSecDate]);
        _currentSecEnum = _imagesByDate[currentSecDate].GetEnumerator();
        _currentSecEnum.MoveNext();

        _currentSecCount = Math.Min(_maxSecNumber, _imagesByDate[currentSecDate].Count);
      }

      _currentSecCount--;
      _images[_currentSecEnum.Current]._shown++;
      _currentImage = _images[_currentSecEnum.Current];

      if (!_currentSecEnum.MoveNext())
        _currentSecCount = 0;

      _shownImages++;
      return _currentImage; 
    }

    public PhotoInfo GetCurrentImage() { return _currentImage; }
    public PhotoInfo[] GetImages() { return _images; }

    private void buildImageSec()
    {
      _images = _imagesTmp.ToArray();
      _imagesByDate = new Dictionary<DateTime, List<int>>();

      _rand = new Random(DateTime.Now.Millisecond);

      for (int i = 0; i < _images.Length; i++)
      {
        DateTime dt = _images[i]._dateTaken == null ? DateTime.MinValue : _images[i]._dateTaken.Value.Date;

        if (!_imagesByDate.ContainsKey(dt))
          _imagesByDate.Add(dt, new List<int>());

        _imagesByDate[dt].Add(i);
      }

      // split long lists to smaller parts
      var dts = _imagesByDate.Keys.ToArray();
      foreach (DateTime dt in dts)
        if (_imagesByDate[dt].Count > _maxSecLength)
        {
          DateTime dtn = dt;
          for (int r = 0; r < _imagesByDate[dt].Count; r += _maxSecLength)
          {
            dtn = dtn.AddSeconds(1.0);

            int cnt = Math.Min(_maxSecLength, _imagesByDate[dt].Count - r);
            _imagesByDate[dtn] = _imagesByDate[dt].GetRange(r, cnt);
          }

          _imagesByDate.Remove(dt);
        }

      _dates = _imagesByDate.Keys.ToArray();
    }

    private static List<T> RandomizeGenericList<T>(IList<T> originalList)
    {
      List<T> randomList = new List<T>();
      Random random = new Random();
      T value = default(T);

      //now loop through all the values in the list
      while (originalList.Count() > 0)
      {
        //pick a random item from th original list
        var nextIndex = random.Next(0, originalList.Count());
        //get the value for that random index
        value = originalList[nextIndex];
        //add item to the new randomized list
        randomList.Add(value);
        //remove value from original list (prevents
        //getting duplicates
        originalList.RemoveAt(nextIndex);
      }

      //return the randomized list
      return randomList;
    }
  }

  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class Screensaver : Window
  {
    private Random _rand;
    private string _path = null;
    private double _updateInterval = 5; // seconds
    private int _fadeSpeed = 200;       // milliseconds
    private int _startOffset = 0;
    private bool _writeStat = false;
    private string _writeStatPath;

    private ImagesInfo _images;
    private DispatcherTimer _switchImage;
    private DispatcherTimer _changeWeatherForecast;
    private Point _mouseLocation = new Point(0, 0);

    private System.Drawing.Rectangle _bounds;
    private cPower _power;

    public Screensaver(System.Drawing.Rectangle bounds, int offset)
    {
      RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\PictureSlideshowScreensaver");
      if (key != null)
      {
        _path = (string)key.GetValue("ImageFolder");
        _updateInterval = double.Parse((string)key.GetValue("Interval"));
        _fadeSpeed = int.Parse((string)key.GetValue("FadeTime"));
        _fadeSpeed = int.Parse((string)key.GetValue("FadeTime"));
        _writeStat = int.Parse((string)key.GetValue("WriteStat")) == 1;
        _writeStatPath = (string)key.GetValue("WriteStatFolder");
      }

      InitializeComponent();
      _bounds = bounds;

      _images = new ImagesInfo();

      _switchImage = new DispatcherTimer();
      _switchImage.Interval = TimeSpan.FromSeconds(_updateInterval + offset);
      _switchImage.Tick += new EventHandler(fade_Tick);

      _changeWeatherForecast = new DispatcherTimer();
      _changeWeatherForecast.Interval = TimeSpan.FromMinutes(1);
      _changeWeatherForecast.Tick += new EventHandler(forecast_Tick);
      forecast_Tick(null, null);

      _startOffset = offset;
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
      _switchImage.Interval = TimeSpan.FromSeconds(_updateInterval);

      DateTime dt = DateTime.Now;
      if (dt.Hour < 7)
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

      // Load images
      if (_path != null)
      {
        // _path = @"E:\PHOTOS\Niagara falls\";
        foreach (var path in _path.Split(";".ToCharArray(), StringSplitOptions.RemoveEmptyEntries))
        {
          bool subdir = false;
          string p = path;
          if (path.EndsWith(@"\*"))
          {
            subdir = true;
            p = path.Substring(0, path.Length - 2);
          }

          addImages(p, subdir);
        }

        if (_images.Count > 0)
        {
          NextImage();

          _switchImage.Start();
        }
      }
      else
      {
        lblUp.Content = "Image folder not set! Please run configuration.";
      }
    }

    private void addImages(string p, bool subdir)
    {
      if (Directory.Exists(p))
      {
        foreach (string s in Directory.GetFiles(p))
        {
          string ss = s.ToLower();
          if (ss.EndsWith(".jpg") || ss.EndsWith(".jpeg"))
          {
            _images.Add(ss);
          }
        }
      }

      if (subdir)
        foreach (string d in Directory.GetDirectories(p))
          addImages(d, subdir);
    }

    private void NextImage()
    {
      PhotoInfo nextphoto = _images.MoveNext();
      if (nextphoto != null)
      {
        try
        {
          PhotoProperties.Date_Photo_Taken = nextphoto._dateTaken == null ? "" : nextphoto._dateTaken.Value.ToString("dd/MM/yyyy");
          if (img1.Opacity == 0)
          {
            SetImage(img1, nextphoto);

            img1.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(_fadeSpeed)));
            img2.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(_fadeSpeed)));
          }
          else if (img2.Opacity == 0)
          {
            SetImage(img2, nextphoto);

            img1.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(_fadeSpeed)));
            img2.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(_fadeSpeed)));
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
    private void SetImage(Image img, PhotoInfo nextphoto)
    {
      if (_power.HasBattery && _power.BatteryLifePercent < 10)
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

        if (!_power.HasBattery || 
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

        if (!_power.HasBattery ||
            (_power.HasBattery && _power.BatteryLifePercent > 20))
        {
          ScaleTransform st = new ScaleTransform(1.0, 1.0, cx, cy);
          DoubleAnimation da = new DoubleAnimation((nextphoto._faces == null ? 1.0 : 1.05) +
                                                   (nextphoto._faces == null ? 0.1 : 0.40) * _rand.NextDouble(), new Duration(TimeSpan.FromSeconds(_updateInterval)));

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
      if (_writeStat)
      {
        string fn = System.IO.Path.Combine(_writeStatPath, string.Format("pss_stat_{0}", DateTime.Now.ToString("MMddHHmm")));
        using (StreamWriter tw = new StreamWriter(fn))
        {
          tw.Write("total pictures: {0}\n", _images.Count);
          tw.Write("shown pictures: {0}\n", _images.Shown);

          PhotoInfo[] imgs = _images.GetImages();
          Array.Sort(imgs, delegate (PhotoInfo ii1, PhotoInfo ii2) {
            return ii1._shown != ii2._shown ? - (ii1._shown.CompareTo(ii2._shown)) : 
                                                 ii1._name.CompareTo(ii2._name); 
          });

          foreach (var img in imgs)
            tw.Write("{0} : [{2}] {1}\n", img._shown, img._name, img._dateTaken.Value.ToString("yyyy-MM-dd"));
        }
      }

      Application.Current.Shutdown();
    }

    private void bExit_Click(object sender, RoutedEventArgs e)
    {
      Shutdown();
    }

    private void lblScreen_MouseMove(object sender, MouseEventArgs e)
    {
      Point newPos = e.GetPosition(this);
      System.Drawing.Point p = new System.Drawing.Point((int)newPos.X, (int)newPos.Y);
      if ((_mouseLocation.X != 0 & _mouseLocation.Y != 0) & ((p.X >= 0 & p.X <= _bounds.Width) & (p.Y >= 0 & p.Y <= _bounds.Height)))
      {
        if (Math.Abs(_mouseLocation.X - newPos.X) > 10 || Math.Abs(_mouseLocation.Y - newPos.Y) > 10)
        {
          // Shutdown();
        }
      }

      _mouseLocation = newPos;
    }

    private void lblScreen_MouseDown(object sender, MouseButtonEventArgs e)
    {
      // Shutdown();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.Key == Key.Escape)
        Shutdown();            
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
    }

    private void WeatherPresenter_Loaded(object sender, RoutedEventArgs e)
    {

    }
  }

}
