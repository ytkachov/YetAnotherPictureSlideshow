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
        foreach (string s in Directory.GetFiles(p))
        {
          string ss = s.ToLower();
          if (ss.EndsWith(".jpg") || ss.EndsWith(".jpeg"))
          {
            Add(ss);
            // Only the scan thread touches _filesFound, so the bare ++ is safe.
            ScanProgressChanged?.Invoke(this, new ScanProgress(++_filesFound, p));
          }
          else if (VideoFileTypes.IsVideo(ss) && !HasPairedPhoto(ss))
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

  private void Add(string name)
  {
    lock (_locker)
    {
      // Cheap during the scan: just record the path (and the paired iPhone
      // .mov if present and wanted). EXIF is read lazily by LocalImageInfo
      // just before the photo is shown — see EnsureMetadataLoaded — so the
      // scan no longer pulls every file over the network.
      string movfile = Path.ChangeExtension(name, "mov");
      bool companion = _settings.ShowLivePhotoVideos && File.Exists(movfile);
      LocalImageInfo ii = new LocalImageInfo(name, companion ? movfile : null, _geocoder, _loader, _finfoStore, _videoMetadata);
      _imagesTmp.Add(ii);
    }
  }

  private static bool HasPairedPhoto(string videoPath)
  {
    // A stat, not a read — cheap even on a network share.
    return File.Exists(Path.ChangeExtension(videoPath, "jpg")) ||
           File.Exists(Path.ChangeExtension(videoPath, "jpeg"));
  }

  // Turns each standalone video into rotation items. The container header
  // carries the duration, so this is where a video that can't be measured
  // drops out of the rotation entirely (logged by the provider) rather than
  // stalling the frame later. A short camera clip becomes one item that plays
  // start to end; a long-form video becomes the slices the virtual video
  // folder deals out.
  private List<LocalVideoClipInfo> BuildVideoClips()
  {
    var clips = new List<LocalVideoClipInfo>();
    if (_videoPaths.Count == 0)
      return clips;

    var chunk = TimeSpan.FromSeconds(_settings.VideoChunkSeconds);
    // A trailing sliver is folded into the chunk before it instead of flashing
    // by on its own; a third of the chunk length is short enough to still feel
    // like a clip.
    var minChunk = TimeSpan.FromSeconds(Math.Max(1.0, _settings.VideoChunkSeconds / 3.0));

    int whole = 0, longForm = 0, slices = 0, livePhotos = 0, longFormOff = 0;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    foreach (var videoPath in _videoPaths)
    {
      bool isLongForm = IsLongForm(videoPath);
      if (isLongForm && _settings.VideoFolderWeight <= 0)
      {
        longFormOff++;
        continue;
      }

      var meta = _videoMetadata.TryRead(videoPath);
      if (meta == null)
        continue;

      // A Live Photo movie whose still is missing: a 2-second fragment, not a
      // recording anyone meant to watch on its own.
      if (meta.IsLivePhoto)
      {
        livePhotos++;
        continue;
      }

      var recorded = meta.CreatedUtc?.ToLocalTime() ?? FileTimestamp(videoPath);
      if (!isLongForm)
      {
        clips.Add(new LocalVideoClipInfo(new VideoClip(videoPath, TimeSpan.Zero, meta.Duration, meta.RotationDegrees), recorded, isLongForm: false));
        whole++;
        continue;
      }

      longForm++;
      foreach (var (start, length) in VideoChunking.Split(meta.Duration, chunk, minChunk))
      {
        clips.Add(new LocalVideoClipInfo(new VideoClip(videoPath, start, length, meta.RotationDegrees), recorded, isLongForm: true));
        slices++;
      }
    }
    sw.Stop();

    Log.Information("Measured {Videos} videos in {Ms} ms: {Whole} clips play whole, {LongForm} long-form videos as {Slices} slices of up to {Chunk}s, {Live} orphaned Live Photo movies skipped",
        _videoPaths.Count, sw.ElapsedMilliseconds, whole, longForm, slices, _settings.VideoChunkSeconds, livePhotos);
    if (longFormOff > 0)
      Log.Information("{Videos} long-form videos stay out of the rotation: VideoFolderWeight is 0", longFormOff);
    return clips;
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

  private static DateTime? FileTimestamp(string path)
  {
    try
    {
      var write = File.GetLastWriteTime(path);
      return write.Year > 1 ? write : (DateTime?)null;
    }
    catch
    {
      // Caption-only data; a video with no readable timestamp just shows none.
      return null;
    }
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

    // The long-form video folder hands out one slice per visit (not a batch
    // of ten) and carries a weight so a couple of minutes of video isn't
    // drowned by tens of thousands of photos — see FolderRotationPolicy.
    var policies = new Dictionary<string, FolderRotationPolicy>(StringComparer.OrdinalIgnoreCase);
    if (_imagesByFolder.ContainsKey(VideoFolderKey))
      policies[VideoFolderKey] = new FolderRotationPolicy(BatchSize: 1, WeightMultiplier: _settings.VideoFolderWeight);

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
