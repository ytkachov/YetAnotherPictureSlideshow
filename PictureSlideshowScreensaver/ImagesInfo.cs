using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using ExifLib;

interface ImagesProvider
{
  void init(string [] parameters);
  ImageInfo GetNext();

}

class ImageInfo
{
  public ImageInfo(string nm) { _name = nm; }
  public string _name;
  public DateTime? _dateTaken;
  public int _shown = 0;
  public UInt16 _orientation = 0;
  public List<System.Drawing.Rectangle> _faces = null;
  public bool _processed = false;
  internal double _dmult;
}


class LocalImages : ImagesProvider
{
  private object _locker = new object();
  private int _currentSecCount;
  private IEnumerator<int> _currentSecEnum;
  private DateTime[] _dates;

  private ImageInfo[] _images;
  private Dictionary<DateTime, List<int>> _imagesByDate;
  private List<ImageInfo> _imagesTmp = new List<ImageInfo>();
  private Random _rand;
  private int _maxSecNumber = 10;
  private int _maxSecLength = 30;
  private int _shownImages = 0;

  public void init(string [] parameters)
  {
    // Load images
    if (_settings._path != null)
    {
      scanForImages(_settings._path);
      Thread.Sleep(1000);

    }
    else
    {
      lblUp.Content = "Image folder not set! Please run configuration.";
    }

  }

  private void scanForImages(string _path)
  {
    throw new NotImplementedException();
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

  private void Add(string name)
  {
    lock (_locker)
    {

      ImageInfo ii = new ImageInfo(name);

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

  private void WriteStat(string write_stat_path)
  {
    lock (_locker)
    {
      string fn = System.IO.Path.Combine(write_stat_path, string.Format("pss_stat_{0}", DateTime.Now.ToString("MM-dd-HHmm")));
      using (StreamWriter tw = new StreamWriter(fn))
      {
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

        foreach (var img in imgidx)
          tw.Write("{0} : [{2}] {1}\n", _images[img]._shown, _images[img]._name, _images[img]._dateTaken != null ? _images[img]._dateTaken.Value.ToString("yyyy-MM-dd") : "---- -- --");
      }
    }
  }

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
