using System;
using System.Windows.Media.Imaging;
using System.Drawing;

public interface ImageInfo
{
  BitmapImage bitmap { get; }
  bool has_accompanying_video { get; }
  string video_name { get; }
  string description { get; }

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
  public ScanProgress(int filesFound, string currentFolder)
  {
    FilesFound = filesFound;
    CurrentFolder = currentFolder;
  }

  public int FilesFound { get; }
  public string CurrentFolder { get; }
}


public interface ImagesProvider
{
  void init(string [] parameters);
  ImageInfo GetNext();
  void WriteStat(string write_stat_path);

  // Raised on the background scan thread as the photo tree is walked, so the
  // UI can show a "scanning…" overlay instead of a black screen on a slow
  // (e.g. SMB) share. Can fire once per file found — subscribers must marshal
  // to the UI thread and throttle.
  event EventHandler<ScanProgress> ScanProgressChanged;
}

