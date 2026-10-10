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
using Yaps.Core.Selection;
using Yaps.Core.Video;
using Yaps.Infrastructure.Images;
using PictureSlideshowScreensaver.Models;

class LocalImages : ImagesProvider
{
  private readonly IGeocoder _geocoder;
  private readonly IImageBitmapLoader _loader;
  private readonly IFinfoStore _finfoStore;
  private readonly Settings _settings;
  private readonly IPhotoStatistics _stats;
  private readonly IVideoMetadataProvider _videoMetadata;

  // Long-form videos (those under Settings.VideoFolders) share one bucket in
  // the rotation, whatever folder they physically live in: "one virtual
  // folder of videos" that takes its turn like any other folder and hands out
  // a slice per visit. Short camera clips don't go here — they stay with the
  // photos of their own folder. The name can't collide with a real directory
  // — ':' is not legal in a Windows path component.
  private const string VideoFolderKey = "::video-clips";

  // Concurrent header reads for long-form videos at startup. Latency-bound
  // (file open over SMB), so a few in flight hide most of it without
  // swamping a NAS.
  private const int VideoProbeParallelism = 4;

  private readonly object _locker = new object();
  private readonly List<LocalImageInfo> _imagesTmp = new List<LocalImageInfo>();
  private readonly List<string> _videoPaths = new List<string>();

  // Directories whose files were already listed. A VideoFolder may sit inside
  // an ImageFolder tree; without this its files would be indexed twice. Only
  // the scan thread touches it.
  private readonly HashSet<string> _listedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  private string[] _longFormRoots = [];

  private string _imagesPath;
  private volatile bool _scanCompleted;
  private int _filesFound;

  public event EventHandler<ScanProgress> ScanProgressChanged;

  private ImageInfo[] _images;
  private Dictionary<string, int[]> _imagesByFolder;
  private PhotoRotation _rotation;

  private int[] _currentBatch;
  private int _currentBatchIdx;

  public LocalImages(IGeocoder geocoder, IImageBitmapLoader loader, IFinfoStore finfoStore, Settings settings,
                     IPhotoStatistics stats, IVideoMetadataProvider videoMetadata)
  {
    _geocoder = geocoder;
    _loader = loader;
    _finfoStore = finfoStore;
    _settings = settings;
    _stats = stats;
    _videoMetadata = videoMetadata;
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
    ImageInfo info;
    lock (_locker)
    {
      if (!_scanCompleted || _rotation == null)
        return null;

      if (_currentBatch == null || _currentBatchIdx >= _currentBatch.Length)
      {
        _currentBatch = _rotation.NextBatch();
        _currentBatchIdx = 0;
        if (_currentBatch.Length == 0)
          return null;
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

    _longFormRoots = NormalizeRoots(_settings.VideoFolders);
    foreach (var root in _longFormRoots)
      addImages(Path.TrimEndingDirectorySeparator(root), true);

    // Video headers are read before the lock is taken: it is file I/O (a few
    // small reads per file, bounded by video count rather than library size),
    // and BuildIndex runs with _locker held.
    var clips = BuildVideoClips();

    string[] paths;
    lock (_locker)
    {
      BuildIndex(clips);
      paths = Array.ConvertAll(_images, i => i.path);
    }

    // Deliberately outside _locker: the first call into the registry loads
    // the JSON file from disk, and GetNext (UI thread) must not queue behind
    // that. The slideshow keeps getting null until _scanCompleted is set.
    _stats.RegisterLibrary(paths);

    lock (_locker)
      _scanCompleted = true;

    sw.Stop();
    Log.Information("Scan completed: {Photos} photos, {Videos} videos as {Clips} clips, across {Folders} folders in {Ms} ms",
        _imagesTmp.Count, _videoPaths.Count, clips.Count, _imagesByFolder?.Count ?? 0, sw.ElapsedMilliseconds);
  }

  private void addImages(string p, bool subdir)
  {
    if (Directory.Exists(p) && _listedDirs.Add(Path.GetFullPath(p)))
    {
      try
      {
        // Pairings (photo + Live Photo movie) are decided from this listing
        // rather than by a File.Exists per file: on an SMB share every stat is
        // a round trip, and the library has tens of thousands of photos.
        string[] files = Directory.GetFiles(p);
        var listed = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        foreach (string s in files)
        {
          string ss = s.ToLower();
          if (ss.EndsWith(".jpg") || ss.EndsWith(".jpeg"))
          {
            Add(ss, listed);
            // Only the scan thread touches _filesFound, so the bare ++ is safe.
            ScanProgressChanged?.Invoke(this, new ScanProgress(++_filesFound, p));
          }
          else if (VideoFileTypes.IsVideo(ss) && !HasPairedPhoto(ss, listed))
          {
            // A video sitting next to a photo of the same name is that photo's
            // Live Photo movie (the pairing in Add below), never an item of its
            // own; only videos that stand alone become rotation items. Same
            // thread as _filesFound, so the plain Add is safe.
            _videoPaths.Add(ss);
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

  private void Add(string name, HashSet<string> listed)
  {
    lock (_locker)
    {
      // Cheap during the scan: just record the path (and the paired iPhone
      // .mov if present and wanted). EXIF is read lazily by LocalImageInfo
      // just before the photo is shown — see EnsureMetadataLoaded — so the
      // scan no longer pulls every file over the network.
      string movfile = Path.ChangeExtension(name, "mov");
      bool companion = _settings.ShowLivePhotoVideos && listed.Contains(movfile);
      LocalImageInfo ii = new LocalImageInfo(name, companion ? movfile : null, _geocoder, _loader, _finfoStore, _videoMetadata);
      _imagesTmp.Add(ii);
    }
  }

  private static bool HasPairedPhoto(string videoPath, HashSet<string> listed)
  {
    return listed.Contains(Path.ChangeExtension(videoPath, "jpg")) ||
           listed.Contains(Path.ChangeExtension(videoPath, "jpeg"));
  }

  // Turns each standalone video into rotation items. A short camera clip is
  // one item however long it runs, so it isn't opened here at all — its
  // header is read just before it's shown (LocalVideoClipInfo). A long-form
  // video has to be measured now: its duration decides how many slices it
  // becomes and so its share of the rotation. One that can't be measured
  // drops out (logged by the provider) rather than stalling the frame later.
  private List<LocalVideoClipInfo> BuildVideoClips()
  {
    var clips = new List<LocalVideoClipInfo>();
    var longFormPaths = new List<string>();
    foreach (var videoPath in _videoPaths)
    {
      if (IsLongForm(videoPath))
        longFormPaths.Add(videoPath);
      else
        clips.Add(new LocalVideoClipInfo(videoPath, _videoMetadata));
    }

    int shortClips = clips.Count;
    if (longFormPaths.Count > 0 && _settings.VideoEveryMinutes <= 0)
    {
      Log.Information("{Videos} long-form videos stay out of the rotation: VideoEveryMinutes is 0", longFormPaths.Count);
      longFormPaths.Clear();
    }

    var sw = System.Diagnostics.Stopwatch.StartNew();
    var metas = MeasureLongForm(longFormPaths);

    var chunk = TimeSpan.FromSeconds(_settings.VideoChunkSeconds);
    // A trailing sliver is folded into the chunk before it instead of flashing
    // by on its own; a third of the chunk length is short enough to still feel
    // like a clip.
    var minChunk = TimeSpan.FromSeconds(Math.Max(1.0, _settings.VideoChunkSeconds / 3.0));

    int longForm = 0, slices = 0;
    for (int i = 0; i < longFormPaths.Count; i++)
    {
      var meta = metas[i];
      if (meta == null || meta.IsLivePhoto)
        continue;

      var recorded = meta.CreatedUtc?.ToLocalTime() ?? LocalVideoClipInfo.FileTimestamp(longFormPaths[i]);
      longForm++;
      foreach (var (start, length) in VideoChunking.Split(meta.Duration, chunk, minChunk))
      {
        clips.Add(new LocalVideoClipInfo(new VideoClip(longFormPaths[i], start, length, meta.RotationDegrees), recorded));
        slices++;
      }
    }
    sw.Stop();

    Log.Information("Videos: {Short} short clips (measured when shown), {LongForm} long-form videos measured into {Slices} slices of up to {Chunk}s in {Ms} ms",
        shortClips, longForm, slices, _settings.VideoChunkSeconds, sw.ElapsedMilliseconds);
    return clips;
  }

  // Opening a file on an SMB share costs tens of milliseconds whatever is
  // read from it, so the headers are read a few at a time; the overlay shows
  // how far along it is.
  private VideoMetadata[] MeasureLongForm(List<string> paths)
  {
    var metas = new VideoMetadata[paths.Count];
    if (paths.Count == 0)
      return metas;

    int done = 0;
    ScanProgressChanged?.Invoke(this, new ScanProgress(_filesFound, Path.GetDirectoryName(paths[0]), 0, paths.Count));
    Parallel.For(0, paths.Count, new ParallelOptions { MaxDegreeOfParallelism = VideoProbeParallelism }, i =>
    {
      metas[i] = _videoMetadata.TryRead(paths[i]);
      int n = Interlocked.Increment(ref done);
      ScanProgressChanged?.Invoke(this, new ScanProgress(_filesFound, paths[i], n, paths.Count));
    });
    return metas;
  }

  private bool IsLongForm(string videoPath)
  {
    if (_longFormRoots.Length == 0)
      return false;

    string full = Path.GetFullPath(videoPath);
    foreach (var root in _longFormRoots)
      if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        return true;
    return false;
  }

  // Full paths ending in a separator, so "Z:\Video" doesn't claim "Z:\Videos2".
  // Accepts the ImageFolder-style "\*" suffix; a long-form folder is always
  // searched with its subfolders.
  private static string[] NormalizeRoots(string[] folders)
  {
    var roots = new List<string>();
    foreach (var folder in folders)
    {
      string f = folder.EndsWith(@"\*") ? folder[..^2] : folder;
      try
      {
        roots.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(f)) + Path.DirectorySeparatorChar);
      }
      catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
      {
        Log.Warning(ex, "Ignoring VideoFolder entry {Folder}", folder);
      }
    }
    return roots.ToArray();
  }

  // A pass shows every photo once, so it lasts about photos x Interval;
  // spreading this many slices over it puts one every VideoEveryMinutes on
  // average. Never more than the folder holds (a pass would then repeat a
  // slice), never less than one.
  private int LongFormVisitsPerPass()
  {
    double passSeconds = _imagesTmp.Count * Math.Max(1.0, _settings._updateInterval);
    int visits = (int)Math.Round(passSeconds / (Math.Max(1, _settings.VideoEveryMinutes) * 60.0));
    return Math.Clamp(visits, 1, Math.Max(1, _imagesByFolder[VideoFolderKey].Length));
  }

  private void BuildIndex(List<LocalVideoClipInfo> clips)
  {
    var items = new ImageInfo[_imagesTmp.Count + clips.Count];
    for (int i = 0; i < _imagesTmp.Count; i++)
      items[i] = _imagesTmp[i];
    for (int i = 0; i < clips.Count; i++)
      items[_imagesTmp.Count + i] = clips[i];

    _images = items;

    var grouped = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < _images.Length; i++)
    {
      // Long-form slices all land in the one virtual folder regardless of
      // where the file sits, so they take their turn as a group. A short clip
      // is just another item of the folder it was shot into.
      string folder = _images[i] is LocalVideoClipInfo { IsLongForm: true }
          ? VideoFolderKey
          : Path.GetDirectoryName(_images[i].path) ?? string.Empty;

      if (!grouped.TryGetValue(folder, out var list))
      {
        list = new List<int>();
        grouped[folder] = list;
      }
      list.Add(i);
    }

    _imagesByFolder = grouped.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.OrdinalIgnoreCase);

    // The long-form video folder hands out one slice per visit and comes up a
    // set number of times per pass, sized so a slice appears about every
    // VideoEveryMinutes — see LongFormVisitsPerPass.
    var policies = new Dictionary<string, FolderRotationPolicy>(StringComparer.OrdinalIgnoreCase);
    if (_imagesByFolder.ContainsKey(VideoFolderKey))
    {
      int visits = LongFormVisitsPerPass();
      policies[VideoFolderKey] = new FolderRotationPolicy(BatchSize: 1, VisitsPerPass: visits);
      Log.Information("Long-form video: {Slices} slices, {Visits} per pass of the photo library (one about every {Minutes} min)",
          _imagesByFolder[VideoFolderKey].Length, visits, _settings.VideoEveryMinutes);
    }

    // Rotation deals folder visits proportionally to folder size and hands
    // out each folder's photos in a deck, so one pass covers the whole
    // library exactly once. Historical show counts come from the registry:
    // after a restart the photos that got least screen time are dealt first.
    _rotation = _imagesByFolder.Count == 0
        ? null
        : new PhotoRotation(_imagesByFolder, id => _stats.GetShowCount(_images[id].path), _settings._photosPerFolder, policies);
    _currentBatch = null;
    _currentBatchIdx = 0;
  }
}
