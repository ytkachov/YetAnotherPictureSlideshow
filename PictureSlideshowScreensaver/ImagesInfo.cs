using System;
using System.Windows.Media.Imaging;
using System.Drawing;
using Yaps.Core.Video;

public interface ImageInfo
{
  BitmapImage bitmap { get; }
  string description { get; }

  // Full path of the photo. The show registry keys on it, so it is also
  // what a failed load is reported under.
  string path { get; }

  // Non-null when this rotation item is a slice of a standalone video rather
  // than a photo: the frame plays it instead of showing a bitmap, and
  // `bitmap` is null.
  VideoClip clip { get; }

  // Non-null when this photo is an iPhone Live Photo whose movie should play
  // first: the frame runs the movie over the still, then dissolves into the
  // still and pans it as usual. Known only after EnsureMetadataLoaded (the
  // movie's header is read there, off the UI thread).
  VideoClip live_video { get; }

  // False once EnsureMetadataLoaded has found the item can't be shown (a
  // video whose header is unreadable, or a Live Photo movie without its
  // still). The slideshow then deals the next item instead.
  bool usable { get; }

  int accent_count { get; }
  RotateFlipType orientation { get; }
  PointF accent { get; }

  // Reads EXIF (orientation / date / GPS) from disk. On a network share this
  // can be a full-file read, so callers run it off the UI thread just before
  // the photo is shown. Idempotent — safe to call more than once.
  void EnsureMetadataLoaded();
}


// Snapshot of an in-progress library scan: how many photos have been found
// so far and which folder is currently being walked. Immutable so it can be
// handed across the scan thread / UI thread boundary safely.
public sealed class ScanProgress
{
  public ScanProgress(int filesFound, string currentFolder, int videosRead = 0, int videosTotal = 0)
  {
    FilesFound = filesFound;
    CurrentFolder = currentFolder;
    VideosRead = videosRead;
    VideosTotal = videosTotal;
  }

  public int FilesFound { get; }
  public string CurrentFolder { get; }

  // Second phase, after the walk: long-form videos being measured. Zero
  // total while the walk is still going.
  public int VideosRead { get; }
  public int VideosTotal { get; }
}


public interface ImagesProvider
{
  void init(string [] parameters);
  ImageInfo GetNext();

  // Raised on the background scan thread as the photo tree is walked, so the
  // UI can show a "scanning…" overlay instead of a black screen on a slow
  // (e.g. SMB) share. Can fire once per file found — subscribers must marshal
  // to the UI thread and throttle.
  event EventHandler<ScanProgress> ScanProgressChanged;
}

