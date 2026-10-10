using System;
using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;
using Serilog;
using Yaps.Core.Abstractions;
using Yaps.Core.Video;

/// <summary>
/// A standalone video — a whole short clip, or one slice of a long-form
/// video — taking its turn in the slideshow. Implements the
/// same <see cref="ImageInfo"/> contract photos do, so the rotation, the show
/// registry and the frame all treat it as an ordinary item — it just answers
/// "I am a clip" instead of handing over a bitmap.
///
/// A long-form slice is measured during the scan (its boundaries depend on
/// the duration). A short clip is one item however long it runs, so its
/// header is read like a photo's EXIF: by <see cref="EnsureMetadataLoaded"/>,
/// on a worker, just before it is shown — the scan never opens it.
/// </summary>
public sealed class LocalVideoClipInfo : ImageInfo
{
  private readonly string _path;
  private readonly IVideoMetadataProvider _videoMetadata;
  private readonly object _metaLock = new();

  // Written once under _metaLock before _loaded is set; read after _loaded
  // (volatile) is seen true, which orders the reads.
  private VideoClip _clip;
  private DateTime? _recorded;
  private bool _usable = true;
  private volatile bool _loaded;

  // A slice of a long-form video, already measured by the scan.
  public LocalVideoClipInfo(VideoClip clip, DateTime? recordedLocal)
  {
    _clip = clip ?? throw new ArgumentNullException(nameof(clip));
    _path = clip.Path;
    _recorded = recordedLocal;
    IsLongForm = true;
    _loaded = true;
  }

  // A short camera clip, measured when it is dealt.
  public LocalVideoClipInfo(string path, IVideoMetadataProvider videoMetadata)
  {
    _path = path ?? throw new ArgumentNullException(nameof(path));
    _videoMetadata = videoMetadata ?? throw new ArgumentNullException(nameof(videoMetadata));
  }

  // A slice of a video from Settings.VideoFolders, rotated through the shared
  // virtual video folder, rather than a camera clip shown with its folder's
  // photos.
  public bool IsLongForm { get; }

  public VideoClip clip => _loaded ? _clip : null;
  public string path => _path;

  public bool usable => !_loaded || _usable;

  public void EnsureMetadataLoaded()
  {
    if (_loaded)
      return;

    lock (_metaLock)
    {
      if (_loaded)
        return;

      var meta = _videoMetadata.TryRead(_path);
      if (meta == null)
      {
        _usable = false;   // the provider logged why
      }
      else if (meta.IsLivePhoto)
      {
        // A Live Photo movie whose still is missing: a 2-second fragment,
        // not a recording anyone meant to watch on its own.
        Log.Debug("Skipping {Video}: Live Photo movie without its still", _path);
        _usable = false;
      }
      else
      {
        _clip = new VideoClip(_path, TimeSpan.Zero, meta.Duration, meta.RotationDegrees);
        _recorded = meta.CreatedUtc?.ToLocalTime() ?? FileTimestamp(_path);
      }

      _loaded = true;
    }
  }

  // No bitmap: the frame's Activate sees a non-null clip and never asks.
  public BitmapImage bitmap => null;

  // This is a video in its own right, not a video attached to a photo.
  public VideoClip live_video => null;

  // Same caption shape as a photo's: the recording date. There is no place
  // name — videos carry no GPS tags we read, and reverse-geocoding one frame
  // of video is not worth a Nominatim call.
  public string description => _loaded ? _recorded?.ToString("dd/MM/yyyy") ?? "" : "";

  // Ken Burns panning and face accents don't apply to moving pictures.
  public int accent_count => 0;
  public PointF accent => new PointF(-1.0F, -1.0F);

  // Rotation for a clip is applied by the player from clip.RotationDegrees,
  // not through the photo pipeline's RotateFlip.
  public RotateFlipType orientation => RotateFlipType.RotateNoneFlipNone;

  // Caption fallback for a container with no creation time.
  public static DateTime? FileTimestamp(string path)
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
}
