using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using File = System.IO.File;
using Yaps.Core.Abstractions;
using Yaps.Core.Models;
using Yaps.Infrastructure.Images;
using PictureSlideshowScreensaver.Models;

class LocalImages : ImagesProvider
{
  private readonly IGeocoder _geocoder;
  private readonly IImageBitmapLoader _loader;
  private readonly IFinfoStore _finfoStore;
  private readonly Settings _settings;
  private readonly IPhotoStatistics _stats;

  private readonly object _locker = new object();
  private readonly List<LocalImageInfo> _imagesTmp = new List<LocalImageInfo>();

  private string _imagesPath;
  private volatile bool _scanCompleted;
  private int _filesFound;

  public event EventHandler<ScanProgress> ScanProgressChanged;

  private LocalImageInfo[] _images;
  private Dictionary<string, int[]> _imagesByFolder;
  private string[] _folders;

  private int[] _currentBatch;
  private int _currentBatchIdx;

  public LocalImages(IGeocoder geocoder, IImageBitmapLoader loader, IFinfoStore finfoStore, Settings settings,
                     IPhotoStatistics stats)
  {
    _geocoder = geocoder;
    _loader = loader;
    _finfoStore = finfoStore;
    _settings = settings;
    _stats = stats;
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
    LocalImageInfo info;
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

      info = _images[_currentBatch[_currentBatchIdx++]];
    }

    // Outside the lock: the registry takes a lock of its own and there is no
    // reason to hold two at once. What is counted here is "picked for
    // display" — a photo that then fails to decode also lands in the
    // registry's failure list, so the report can tell the two apart.
    _stats.RecordShown(info.path);
    return info;
  }

  private void scanForImages()
  {
    var sw = System.Diagnostics.Stopwatch.StartNew();
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

    string[] paths;
    lock (_locker)
    {
      BuildIndex();
      paths = Array.ConvertAll(_images, i => i.path);
    }

    // Deliberately outside _locker: the first call into the registry loads
    // the JSON file from disk, and GetNext (UI thread) must not queue behind
    // that. The slideshow keeps getting null until _scanCompleted is set.
    _stats.RegisterLibrary(paths);

    lock (_locker)
      _scanCompleted = true;

    sw.Stop();
    Log.Information("Scan completed: {Photos} photos across {Folders} folders in {Ms} ms",
        _images?.Length ?? 0, _folders?.Length ?? 0, sw.ElapsedMilliseconds);
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
          {
            Add(ss);
            // Only the scan thread touches _filesFound, so the bare ++ is safe.
            ScanProgressChanged?.Invoke(this, new ScanProgress(++_filesFound, p));
          }
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
      // Cheap during the scan: just record the path (and the paired iPhone
      // .mov if present). EXIF is read lazily by LocalImageInfo just before
      // the photo is shown — see EnsureMetadataLoaded — so the scan no longer
      // pulls every file over the network.
      string movfile = Path.ChangeExtension(name, "mov");
      LocalImageInfo ii = new LocalImageInfo(name, File.Exists(movfile) ? movfile : null, _geocoder, _loader, _finfoStore);
      _imagesTmp.Add(ii);
    }
  }

  private void BuildIndex()
  {
    _images = _imagesTmp.ToArray();

    var grouped = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < _images.Length; i++)
    {
      string folder = Path.GetDirectoryName(_images[i].path) ?? string.Empty;
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
