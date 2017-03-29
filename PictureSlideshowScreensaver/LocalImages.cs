using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using ExifLib;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Windows.Interop;
using Emgu.CV;

class LocalImageInfo : ImageInfo
{
  public LocalImageInfo(string nm) { _name = nm; }

  internal string _name;
  internal DateTime? _dateTaken;
  internal int _shown = 0;
  internal UInt16 _orientation = 0;

  private List<System.Drawing.Rectangle> _faces = null;
  private bool _processed = false;
  private double _dmult;
  private int _pixel_width, _pixel_height;
  private System.Drawing.Bitmap _bitmap;
  private static Random _rand = new Random(DateTime.Now.Millisecond);

  public string description
  {
    get
    {
      // return _name + " :: " + (_dateTaken == null ? "" : _dateTaken.Value.ToString("dd/MM/yyyy"));
      return _dateTaken == null ? "" : _dateTaken.Value.ToString("dd/MM/yyyy");
    }
  }

  public BitmapImage bitmap
  {
    get
    {
      BitmapImage bmp_img = new BitmapImage(new Uri(_name));

      using (MemoryStream outStream = new MemoryStream())
      {
        BitmapEncoder enc = new BmpBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp_img));
        enc.Save(outStream);
        _bitmap = new System.Drawing.Bitmap(outStream);

        System.Drawing.RotateFlipType rf = System.Drawing.RotateFlipType.RotateNoneFlipNone;
        switch (_orientation)
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
          _bitmap.RotateFlip(rf);
          bmp_img = Bitmap2BitmapImage(_bitmap);
        }
      }

      _pixel_width = bmp_img.PixelWidth;
      _pixel_height = bmp_img.PixelHeight;

      return bmp_img;
    }
  }

  public int accent_count
  {
    get
    {
      FindFaces();
      return _faces != null ? _faces.Count : 0;
    }
  }

  public PointF accent
  {
    get
    {
      FindFaces();
      return get_accent(_rand.Next(_faces.Count));
    }
  }

  public PointF get_accent(int acc)
  {
    float fx = -1.0F, fy = -1.0F;
    if (_faces != null && _faces.Count != 0 && acc >= 0 && acc < _faces.Count)
    {
      fx = (float)((_faces[acc].Right + _faces[acc].Left) * _dmult / 2.0 - _pixel_width / 2.0);
      fy = (float)((_faces[acc].Top + _faces[acc].Bottom) * _dmult / 2.0 - _pixel_height / 2.0);
    }

    return new PointF(fx, fy);
  }

  public void ReleaseResources()
  {
    if (_bitmap != null)
    {
      _bitmap.Dispose();
      _bitmap = null;
    }
  }

  private void FindFaces()
  {

    if (!_processed)
    {
      _processed = true;
      _dmult = 3.0;
      List<System.Drawing.Rectangle> faces = new List<System.Drawing.Rectangle>();

      System.Drawing.Bitmap b = new System.Drawing.Bitmap((int)(bitmap.Width / _dmult), (int)(bitmap.Height / _dmult), System.Drawing.Imaging.PixelFormat.Format24bppRgb);
      using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage((System.Drawing.Image)b))
      {
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(_bitmap, 0, 0, b.Width, b.Height);

        try
        {
          Image<Emgu.CV.Structure.Bgr, byte> cvimg = new Image<Emgu.CV.Structure.Bgr, byte>(b);
          Mat cvmat = new Mat(cvimg.Mat, new System.Drawing.Rectangle(new System.Drawing.Point(0, 0), cvimg.Size));

          long detectionTime;
          FaceDetection.DetectFace.Detect(cvmat, "haarcascade_frontalface_alt2.xml", faces, out detectionTime);

          if (faces.Count != 0)
            _faces = faces;

        }
        catch (Exception e)
        {
          string err = e.Message;
        }
      }
    }
  }

  private BitmapImage Bitmap2BitmapImage(System.Drawing.Bitmap bitmap)
  {
    BitmapImage bitmapImage = new BitmapImage();
    using (MemoryStream outStream = new MemoryStream())
    {
      bitmap.Save(outStream, System.Drawing.Imaging.ImageFormat.Bmp);
      outStream.Position = 0;
      bitmapImage.BeginInit();
      bitmapImage.StreamSource = outStream;
      bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
      bitmapImage.EndInit();
    }

    return (BitmapImage)bitmapImage;
  }

}


class LocalImages : ImagesProvider
{
  private object _locker = new object();
  private int _currentSecCount;
  private IEnumerator<int> _currentSecEnum;
  private DateTime[] _dates;
  private string _imagesPath;

  private LocalImageInfo[] _images;
  private Dictionary<DateTime, List<int>> _imagesByDate;
  private List<LocalImageInfo> _imagesTmp = new List<LocalImageInfo>();
  private Random _rand;
  private int _maxSecNumber = 10;
  private int _maxSecLength = 30;
  private int _shownImages = 0;
  private List<string> _messages = new List<string>();

  public void init(string[] parameters)
  {
    // Load images
    if (parameters.Length > 0)
    {
      _imagesPath = parameters[0];
      if (_imagesPath != null)
      {
        Thread imgscaner = new Thread(new ThreadStart(scanForImages)) { IsBackground = true };
        imgscaner.Start();

        Thread.Sleep(1000);
      }
    }
  }

  public ImageInfo GetNext()
  {
    ImageInfo currentImage = null;

    lock (_locker)
    {
      if (_images == null || _images.Length != _imagesTmp.Count)
        buildImageSec();

      if (_images.Length != 0)
      {
        if (_currentSecCount <= 0)
        {
          // build new sequence
          DateTime currentSecDate = _dates[_rand.Next(0, _dates.Length)];

          _imagesByDate[currentSecDate] = RandomizeGenericList(_imagesByDate[currentSecDate]);
          _currentSecEnum = _imagesByDate[currentSecDate].GetEnumerator();
          _currentSecEnum.MoveNext();

          _currentSecCount = Math.Min(_maxSecNumber, _imagesByDate[currentSecDate].Count);
        }

        _currentSecCount--;
        _images[_currentSecEnum.Current]._shown++;
        currentImage = _images[_currentSecEnum.Current];

        if (!_currentSecEnum.MoveNext())
          _currentSecCount = 0;

        _shownImages++;
      }
    }

    return currentImage;
  }

  public void WriteStat(string write_stat_path)
  {
    lock (_locker)
    {
      string fn = System.IO.Path.Combine(write_stat_path, string.Format("pss_stat_{0}", DateTime.Now.ToString("MM-dd-HHmm")));
      using (StreamWriter tw = new StreamWriter(fn))
      {
        foreach (string s in _messages)
          tw.WriteLine(s);

        tw.Write("total pictures: {0}\n", _images.Length);
        tw.Write("shown pictures: {0}\n", _shownImages);

        int[] imgidx = new int[_images.Length];
        for (int i = 0; i < _images.Length; i++)
          imgidx[i] = i;

        Array.Sort(imgidx, delegate (int ii1, int ii2)
        {
          return _images[ii1]._shown != _images[ii2]._shown ? -(_images[ii1]._shown.CompareTo(_images[ii2]._shown)) :
                 _images[ii1]._name.CompareTo(_images[ii2]._name);
        });

        Dictionary<int, int> freq = new Dictionary<int, int>();
        foreach (var img in _images)
        {
          if (!freq.ContainsKey(img._shown))
            freq.Add(img._shown, 0);

          freq[img._shown]++;
        }

        foreach (var f in freq)
          tw.Write("shown {0} times : [{1}] images\n", f.Key, f.Value);

        foreach (var img in imgidx)
          tw.Write("{0} : [{2}] {1}\n", _images[img]._shown, _images[img]._name, _images[img]._dateTaken != null ? _images[img]._dateTaken.Value.ToString("yyyy-MM-dd") : "---- -- --");
      }
    }
  }

  private void scanForImages()
  {
    foreach (var path in _imagesPath.Split(";".ToCharArray(), StringSplitOptions.RemoveEmptyEntries))
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
  }

  private void addImages(string p, bool subdir)
  {
    if (Directory.Exists(p))
    {
      foreach (string s in Directory.GetFiles(p))
      {
        string ss = s.ToLower();
        if (ss.EndsWith(".jpg") || ss.EndsWith(".jpeg"))
          Add(ss);
      }
    }

    if (subdir)
      foreach (string d in Directory.GetDirectories(p))
        addImages(d, subdir);
  }

  private void Add(string name)
  {
    lock (_locker)
    {
      LocalImageInfo ii = new LocalImageInfo(name);

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
  }

  private void buildImageSec()
  {
    _messages.Add(string.Format("Images current {0} new {1}", _images == null ? 0: _images.Length, _imagesTmp.Count));

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
