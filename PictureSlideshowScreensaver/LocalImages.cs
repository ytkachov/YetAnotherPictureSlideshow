using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using ExifLibrary;
using File = System.IO.File;
using Yaps.Core.Abstractions;
using Yaps.Core.Models;
using Yaps.Infrastructure.Faces;
using PictureSlideshowScreensaver.Models;

class LocalImages : ImagesProvider
{
  private readonly IGeocoder _geocoder;
  private readonly IFaceDetector _faceDetector;
  private readonly IFinfoStore _finfoStore;
  private readonly Settings _settings;

  private readonly object _locker = new object();
  private readonly List<LocalImageInfo> _imagesTmp = new List<LocalImageInfo>();
  private readonly List<string> _messages = new List<string>();

  private string _imagesPath;
  private volatile bool _scanCompleted;

  private LocalImageInfo[] _images;
  private Dictionary<string, int[]> _imagesByFolder;
  private string[] _folders;

  private int[] _currentBatch;
  private int _currentBatchIdx;

  private int _shownImages;

  public LocalImages(IGeocoder geocoder, IFaceDetector faceDetector, IFinfoStore finfoStore, Settings settings)
  {
    _geocoder = geocoder;
    _faceDetector = faceDetector;
    _finfoStore = finfoStore;
    _settings = settings;
  }

  public void init(string[] parameters)
  {
    if (parameters.Length == 0)
      return;

    _imagesPath = parameters[0];
    if (string.IsNullOrEmpty(_imagesPath))
      return;

    // Background scan — must not block UI startup. GetNext() gates on
    // _scanCompleted, so the slideshow simply waits (returns null) until
    // the full index is built, and never shows photos against a partially
    // filled _imagesTmp.
    Task.Run(scanForImages);
  }

  public ImageInfo GetNext()
  {
    lock (_locker)
    {
      if (!_scanCompleted || _folders == null || _folders.Length == 0)
        return null;

      if (_currentBatch == null || _currentBatchIdx >= _currentBatch.Length)
      {
        string folder = _folders[Random.Shared.Next(_folders.Length)];
        int[] src = _imagesByFolder[folder];
        int take = Math.Min(_settings._photosPerFolder, src.Length);
        _currentBatch = PickRandomSubset(src, take);
        _currentBatchIdx = 0;
      }

      var info = _images[_currentBatch[_currentBatchIdx++]];
      info._shown++;
      _shownImages++;
      return info;
    }
  }

  public void WriteStat(string write_stat_path)
  {
    lock (_locker)
    {
      if (!Directory.Exists(write_stat_path) || _images == null)
        return;

      string fn = Path.Combine(write_stat_path, string.Format("pss_stat_{0}", DateTime.Now.ToString("yyyy-MM-dd-HHmm")));
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

    lock (_locker)
    {
      BuildIndex();
      _scanCompleted = true;
    }
  }

  private void addImages(string p, bool subdir)
  {
    if (Directory.Exists(p))
    {
      try
      {
        foreach (string s in Directory.GetFiles(p))
        {
          string ss = s.ToLower();
          if (ss.EndsWith(".jpg") || ss.EndsWith(".jpeg"))
            Add(ss);
        }
      }
      catch (Exception ex)
      {
        Log.Error(ex, "ex:1");
      }
    }

    try
    {
      if (subdir)
        foreach (string d in Directory.GetDirectories(p))
          addImages(d, subdir);
    }
    catch (Exception ex)
    {
      Log.Error(ex, "ex:2");
    }
  }

  private void Add(string name)
  {
    lock (_locker)
    {
      // special treatment for iPhone photo-video pair
      string movfile = Path.ChangeExtension(name, "mov");
      LocalImageInfo ii = new LocalImageInfo(name, File.Exists(movfile) ? movfile : null, _geocoder, _faceDetector, _finfoStore);

      // If a previous run flagged this image as having unreadable EXIF, skip the read.
      string finfoPath = Path.ChangeExtension(name, "finfo");
      var existing = _finfoStore.Read(finfoPath);
      if (existing != null && existing.ExifReadFailed)
      {
        _imagesTmp.Add(ii);
        return;
      }

      try
      {
        var reader = ImageFile.FromFile(name);

        var orientation = reader.Properties.Get<ExifUShort>(ExifTag.Orientation);
        if (orientation != null)
          ii._orientation = orientation;

        ExifDateTime eDatePicture = reader.Properties.Get<ExifDateTime>(ExifTag.DateTime);
        if (eDatePicture != null)
          ii._dateTaken = eDatePicture;
        else
        {
          eDatePicture = reader.Properties.Get<ExifDateTime>(ExifTag.DateTimeOriginal);
          if (eDatePicture != null)
            ii._dateTaken = eDatePicture;
        }

        try
        {
          var latProp = reader.Properties[ExifTag.GPSLatitude];
          var latRefProp = reader.Properties[ExifTag.GPSLatitudeRef];
          var lonProp = reader.Properties[ExifTag.GPSLongitude];
          var lonRefProp = reader.Properties[ExifTag.GPSLongitudeRef];

          if (latProp?.Value is Array latArr && latArr.Length == 3 &&
              lonProp?.Value is Array lonArr && lonArr.Length == 3)
          {
            dynamic latD = latArr.GetValue(0), latM = latArr.GetValue(1), latS = latArr.GetValue(2);
            double lat = (double)latD.Numerator / (double)latD.Denominator +
                         (double)latM.Numerator / (double)latM.Denominator / 60.0 +
                         (double)latS.Numerator / (double)latS.Denominator / 3600.0;

            dynamic lonD = lonArr.GetValue(0), lonM = lonArr.GetValue(1), lonS = lonArr.GetValue(2);
            double lon = (double)lonD.Numerator / (double)lonD.Denominator +
                         (double)lonM.Numerator / (double)lonM.Denominator / 60.0 +
                         (double)lonS.Numerator / (double)lonS.Denominator / 3600.0;

            var latRef = latRefProp?.Value?.ToString();
            var lonRef = lonRefProp?.Value?.ToString();
            if (latRef == "S" || latRef == "South") lat = -lat;
            if (lonRef == "W" || lonRef == "West") lon = -lon;

            if (!double.IsNaN(lat) && !double.IsNaN(lon) && !double.IsInfinity(lat) && !double.IsInfinity(lon))
            {
              ii._latitude = lat;
              ii._longitude = lon;
            }
          }
        }
        catch (Exception ex2)
        {
          Log.Error(ex2, $"GPS EXIF failed for {name}");
        }
      }
      catch (Exception ex)
      {
        Log.Error(ex, $"Image: {name}");

        ii._messages.Add("Exeption " + ex.ToString());
      }

      _imagesTmp.Add(ii);
    }
  }

  private void BuildIndex()
  {
    _messages.Add(string.Format("Images: {0}", _imagesTmp.Count));

    _images = _imagesTmp.ToArray();

    var grouped = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < _images.Length; i++)
    {
      string folder = Path.GetDirectoryName(_images[i]._name) ?? string.Empty;
      if (!grouped.TryGetValue(folder, out var list))
      {
        list = new List<int>();
        grouped[folder] = list;
      }
      list.Add(i);
    }

    _imagesByFolder = grouped.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    _folders = _imagesByFolder.Keys.ToArray();
  }

  // Partial Fisher-Yates: produces a shuffled prefix of length `take`
  // without copying the full source twice or shuffling a long folder
  // every time it is picked.
  private static int[] PickRandomSubset(int[] src, int take)
  {
    int[] copy = (int[])src.Clone();
    int n = copy.Length;
    int limit = Math.Min(take, n);
    for (int i = 0; i < limit; i++)
    {
      int j = Random.Shared.Next(i, n);
      (copy[i], copy[j]) = (copy[j], copy[i]);
    }

    int[] result = new int[limit];
    Array.Copy(copy, result, limit);
    return result;
  }
}
