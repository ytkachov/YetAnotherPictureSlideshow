using System;
using System.Drawing;
using System.Windows.Media.Imaging;
using Yaps.Core.Video;

/// <summary>
/// A standalone video — a whole short clip, or one slice of a long-form
/// video — taking its turn in the slideshow. Implements the
/// same <see cref="ImageInfo"/> contract photos do, so the rotation, the show
/// registry and the frame all treat it as an ordinary item — it just answers
/// "I am a clip" instead of handing over a bitmap.
///
/// Everything here is known at scan time (the MP4 header was already parsed to
/// work out the chunk boundaries), so there is no lazy metadata load and no
/// per-show file access beyond what the player itself does.
/// </summary>
public sealed class LocalVideoClipInfo : ImageInfo
{
  private readonly VideoClip _clip;
  private readonly DateTime? _recorded;

  public LocalVideoClipInfo(VideoClip clip, DateTime? recordedLocal, bool isLongForm)
  {
    _clip = clip ?? throw new ArgumentNullException(nameof(clip));
    _recorded = recordedLocal;
    IsLongForm = isLongForm;
  }

  // A slice of a video from Settings.VideoFolders, rotated through the shared
  // virtual video folder, rather than a camera clip shown with its folder's
  // photos.
  public bool IsLongForm { get; }

  public VideoClip clip => _clip;
  public string path => _clip.Path;

  // No bitmap: the frame's Activate sees a non-null clip and never asks.
  public BitmapImage bitmap => null;

  // This is a video in its own right, not a video attached to a photo.
  public VideoClip live_video => null;

  // Same caption shape as a photo's: the recording date. There is no place
  // name — videos carry no GPS tags we read, and reverse-geocoding one frame
  // of video is not worth a Nominatim call.
  public string description => _recorded?.ToString("dd/MM/yyyy") ?? "";

  // Ken Burns panning and face accents don't apply to moving pictures.
  public int accent_count => 0;
  public PointF accent => new PointF(-1.0F, -1.0F);

  // Rotation for a clip is applied by the player from clip.RotationDegrees,
  // not through the photo pipeline's RotateFlip.
  public RotateFlipType orientation => RotateFlipType.RotateNoneFlipNone;

  public void EnsureMetadataLoaded()
  {
    // Nothing to do — the scan already read the container header.
  }
}
