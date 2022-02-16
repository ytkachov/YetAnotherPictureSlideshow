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
      if (!Directory.Exists(write_stat_path))
        return;

      string fn = Path.Combine(write_stat_path, string.Format("pss_stat_{0}", DateTime.Now.ToString("MM-dd-HHmm")));
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
      // special treatment for iPhone photo-video pair
      string movfile = Path.ChangeExtension(name, "mov");
      LocalImageInfo ii = new LocalImageInfo(name, File.Exists(movfile) ? movfile : null);

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
        ii._messages.Add("Exeption " + ex.ToString());
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
