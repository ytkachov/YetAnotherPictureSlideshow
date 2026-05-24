using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Windows.Interop;
using ExifLibrary;
using Serilog;
using Yaps.Core.Abstractions;
using Yaps.Core.Models;
using Yaps.Infrastructure.Faces;
using Yaps.Infrastructure.Orientation;

public class LocalImageInfo : ImageInfo
{
  internal string _name;
  internal string _video_name;  // for iPhone accompanying video file
  internal DateTime? _dateTaken;
  internal int _shown = 0;
  internal UInt16 _orientation = 0;
  // Mirror of finfo.OrientationDetectionAttempted, populated in ReadExif.
  // When true the bitmap getter skips the live ONNX detector — the model was
  // already evaluated for this photo by OrientationTagger or a prior show.
  private bool _orientationAttempted = false;

  internal List<string> _messages = new List<string>();

  private List<PointF> _faces = null;
  private volatile bool _processed = false;
  private readonly object _metaLock = new object();
  private volatile bool _metadataLoaded = false;
  internal double? _latitude = null;
  internal double? _longitude = null;
  internal string _placeName = null;
  private readonly IGeocoder _geocoder;
  private readonly IFaceDetector _faceDetector;
  private readonly IOrientationDetector _orientationDetector;
  private readonly IFinfoStore _finfoStore;

  // Actionable EXIF codes for live detection — mirrors OrientationTagger's
  // policy (the model is unreliable on 180° = code 3, so it's recorded as
  // attempted-but-noop). Confidence floor mirrors the same default.
  private static readonly HashSet<int> _actionableCodes = new HashSet<int> { 6, 8 };
  private const double _orientationMinConfidence = 0.5;

  public LocalImageInfo(string nm, string videoname = null, IGeocoder geocoder = null, IFaceDetector faceDetector = null, IOrientationDetector orientationDetector = null, IFinfoStore finfoStore = null)
  {
    _name = nm;
    _video_name = videoname;
    _geocoder = geocoder;
    _faceDetector = faceDetector;
    _orientationDetector = orientationDetector;
    _finfoStore = finfoStore ?? new FileFinfoStore();
  }

  public void EnsureMetadataLoaded()
  {
    if (_metadataLoaded)
      return;

    lock (_metaLock)
    {
      if (_metadataLoaded)
        return;

      ReadExif();
      _metadataLoaded = true;
    }
  }

  // EXIF used to be read for every photo up front during the library scan,
  // which meant pulling every file (often whole multi-MB JPEGs) over the
  // network before the first photo could appear. The slideshow index only
  // needs the file path, so the read is now deferred to just before display.
  private void ReadExif()
  {
    // A previous GeoTagger run may have flagged this file's EXIF as
    // unreadable; honour that and skip the (failing) read. The store resolves
    // the .finfo location (next to the photo, or a configured finfo folder).
    var existing = _finfoStore.Read(_name);

    // A corrected orientation in .finfo (written by tools/orientation) wins
    // over EXIF: this library's EXIF Orientation is always Normal/absent even
    // for visibly sideways photos, so the sidecar is the authoritative source.
    // Applied before the early return so it survives even when EXIF is skipped.
    bool orientationFromFinfo = existing?.Orientation is int finfoOrientation
                                && finfoOrientation is >= 1 and <= 8;
    if (orientationFromFinfo)
      _orientation = (ushort)existing!.Orientation!.Value;

    // Track whether the ONNX orientation model was already run on this photo
    // (either via OrientationTagger or a prior live show) so the bitmap getter
    // doesn't re-run it. Set together with the corrected Orientation value.
    _orientationAttempted = existing?.OrientationDetectionAttempted == true || orientationFromFinfo;

    if (existing != null && existing.ExifReadFailed)
      return;

    try
    {
      var reader = ImageFile.FromFile(_name);

      // Fall back to the EXIF Orientation tag only when .finfo did not specify
      // one. ExifLibrary exposes it as ExifEnumProperty<Orientation>, not
      // ExifUShort, so the old Get<ExifUShort> always returned null and
      // rotation was never applied; the enum's numeric value is the code 1-8.
      if (!orientationFromFinfo &&
          reader.Properties[ExifTag.Orientation]?.Value is Orientation exifOrientation)
        _orientation = (ushort)exifOrientation;

      // Prefer the capture time (DateTimeOriginal) over the file-edit time
      // (DateTime): re-saved / exported photos carry an edit date in DateTime
      // that is not when the shot was taken. Each candidate is validated
      // because some files store a garbage 0001-01-01 original. Fall back to
      // the file's own timestamp when no usable EXIF date is present.
      _dateTaken =
          ReadValidDate(reader, ExifTag.DateTimeOriginal) ??
          ReadValidDate(reader, ExifTag.DateTimeDigitized) ??
          ReadValidDate(reader, ExifTag.DateTime) ??
          FileTimestampFallback(_name);

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
            _latitude = lat;
            _longitude = lon;
          }
        }
      }
      catch (Exception ex2)
      {
        Log.Error(ex2, $"GPS EXIF failed for {_name}");
      }
    }
    catch (Exception ex)
    {
      Log.Error(ex, $"Image: {_name}");
      _messages.Add("Exeption " + ex.ToString());
    }
  }

  // Reads one EXIF date tag and rejects values that are null or obviously
  // bogus (year <= 1), which some cameras write into DateTimeOriginal.
  private static DateTime? ReadValidDate(ImageFile reader, ExifTag tag)
  {
    var prop = reader.Properties.Get<ExifDateTime>(tag);
    if (prop == null)
      return null;

    DateTime value = prop; // ExifDateTime -> DateTime (implicit)
    return value.Year > 1 ? value : (DateTime?)null;
  }

  // Last resort when a photo carries no usable EXIF date (~8% of the
  // library). The earlier of write/creation time is the closest proxy for
  // capture time; it can reflect a copy, so it is used only as a fallback.
  private static DateTime? FileTimestampFallback(string path)
  {
    try
    {
      var write = File.GetLastWriteTime(path);
      var create = File.GetCreationTime(path);
      var earliest = write < create ? write : create;
      return earliest.Year > 1 ? earliest : (DateTime?)null;
    }
    catch
    {
      return null;
    }
  }

  public RotateFlipType orientation
  {
    get
    {
      var rf = System.Drawing.RotateFlipType.RotateNoneFlipNone;
      switch (_orientation)
      {
        case 1: break;
        case 2: rf = System.Drawing.RotateFlipType.RotateNoneFlipX; break;
        case 3: rf = System.Drawing.RotateFlipType.Rotate180FlipNone; break;
        case 4: rf = System.Drawing.RotateFlipType.RotateNoneFlipY; break;
        case 5: rf = System.Drawing.RotateFlipType.Rotate90FlipX; break;
        case 6: rf = System.Drawing.RotateFlipType.Rotate90FlipNone; break;
        case 7: rf = System.Drawing.RotateFlipType.Rotate270FlipX; break;
        case 8: rf = System.Drawing.RotateFlipType.Rotate270FlipNone; break;
      }

      return rf;
    }
  }


  public bool has_accompanying_video
  {
    get
    {
      return _video_name != null && _name != null;
    }
  }
  public string video_name
  {
    get
    {
      return _video_name;
    }
  }

  public string description
  {
    get
    {
      // _placeName may be written by the fire-and-forget geocoding Task in
      // FindFaces; Volatile.Read pairs with the Volatile.Write there to
      // guarantee the UI thread sees the latest value once it lands.
      string place = Volatile.Read(ref _placeName);
      string d = (_dateTaken == null ? "" : _dateTaken.Value.ToString("dd/MM/yyyy"));
      if (!string.IsNullOrEmpty(place))
      {
        if (!string.IsNullOrEmpty(d))
          d += " :: ";
        d += place;
      }
      return d;
    }
  }

  public BitmapImage bitmap
  {
    get
    {
      BitmapImage bmp_img = new BitmapImage(new Uri(_name));

      // Live orientation detection runs when the photo has no recorded
      // orientation AND the model wasn't already evaluated for it. After the
      // backfill (Attempted=true across the library) this only fires on new
      // photos added since. Decided up front so the fast path stays correct.
      bool needsOrientationDetection = _orientationDetector != null
                                       && _orientation == 0
                                       && !_orientationAttempted;

      // Fast path: no rotation needed and face detection already cached.
      if (!needsOrientationDetection
          && orientation == RotateFlipType.RotateNoneFlipNone
          && _processed)
      {
        bmp_img.Freeze();
        return bmp_img;
      }

      using (MemoryStream outStream = new MemoryStream())
      {
        BitmapEncoder enc = new BmpBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp_img));
        enc.Save(outStream);

        // System.Drawing.Bitmap was previously leaked — wrap in using so the
        // unmanaged GDI handle is freed even if FindFaces throws.
        using (Bitmap bitmap = new Bitmap(outStream))
        {
          // Detect on the loaded-but-not-yet-rotated pixels, so the result
          // matches the standard EXIF orientation contract. Must happen BEFORE
          // RotateFlip and BEFORE FindFaces (the latter detects faces on the
          // rotated bitmap and would otherwise cache them in the wrong frame).
          if (needsOrientationDetection)
            DetectAndPersistOrientation(bitmap);

          bitmap.RotateFlip(orientation);

          bmp_img = Bitmap2BitmapImage(bitmap);

          FindFaces(bitmap);
        }
      }

      return bmp_img;
    }
  }

  public int accent_count
  {
    get
    {
      return _faces != null ? _faces.Count : 0;
    }
  }

  public PointF accent
  {
    get
    {
      PointF pt = new PointF(-1.0F, -1.0F);
      // Snapshot the reference so a concurrent FindFaces assignment cannot
      // shrink the list under our feet.
      var faces = _faces;
      if (faces != null && faces.Count != 0)
      {
        int acc = Random.Shared.Next(faces.Count);
        if (acc >= 0 && acc < faces.Count)
          pt = faces[acc];
      }

      return pt;
    }
  }

  // Runs the ONNX orientation detector once for a photo, persists the result
  // into .finfo (merging so existing fields survive), and sets _orientation
  // when the prediction is actionable. Mirrors OrientationTagger's policy:
  // only EXIF codes 6 and 8 with confidence >= 0.5 are applied; everything
  // else (180 = code 3 / upright / low confidence) is recorded as
  // attempted-but-noop so the model is never run on this photo again.
  private void DetectAndPersistOrientation(Bitmap pixels)
  {
    try
    {
      var result = _orientationDetector.Detect(pixels);
      bool actionable = _actionableCodes.Contains(result.Code)
                        && result.Confidence >= _orientationMinConfidence;

      if (actionable)
        _orientation = (ushort)result.Code;
      _orientationAttempted = true;

      var data = _finfoStore.Read(_name) ?? new FinfoData();
      data.OrientationDetectionAttempted = true;
      if (actionable)
      {
        data.Orientation = result.Code;
        // Any cached faces were detected against the un-rotated frame and
        // would land in the wrong place once the screensaver rotates the
        // photo. Drop them; FindFaces will recompute on the rotated bitmap
        // (and merge back, preserving the new Orientation).
        data.Faces = null;
      }
      _finfoStore.Write(_name, data);
    }
    catch (Exception ex)
    {
      Log.Error(ex, "Live orientation detection failed for {Image}", _name);
      // Still mark attempted in-memory so we don't keep trying on every show
      // of the same photo in this process. The next process start may retry.
      _orientationAttempted = true;
    }
  }

  private void FindFaces(Bitmap bitmap)
  {
    if (!_processed && bitmap != null)
    {
      double dmult = 3.0;
      int pixel_width = bitmap.Width;
      int pixel_height = bitmap.Height;

      // Read any existing sidecar up front. A non-null Faces array (even an
      // empty one — meaning "detected, none found") is an authoritative cache
      // and we skip detection. A missing Faces array means faces were never
      // computed, or were invalidated after an orientation change in .finfo
      // (tools/orientation clears Faces so we re-detect on the rotated image).
      FinfoData existing = null;
      try
      {
        // IFinfoStore swallows JSON errors and returns null, but any
        // exception escaping from here propagates out of the bitmap getter
        // and the photo disappears from the slideshow — guard it.
        existing = _finfoStore.Read(_name);
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Failed to load cached .finfo for {Image}", _name);
      }

      if (existing != null)
      {
        _placeName = existing.PlaceName;
        if (existing.Latitude != null) _latitude = existing.Latitude;
        if (existing.Longitude != null) _longitude = existing.Longitude;

        if (existing.Faces != null)
        {
          if (existing.Faces.Length != 0)
          {
            _faces = new List<PointF>();
            foreach (var f in existing.Faces)
              _faces.Add(new PointF((float)((f.Right + f.Left) * dmult / 2.0 - pixel_width / 2.0),
                                    (float)((f.Top + f.Bottom) * dmult / 2.0 - pixel_height / 2.0)));
          }

          _processed = true;
          return;
        }
      }

      System.Drawing.Bitmap b = new System.Drawing.Bitmap((int)(pixel_width / dmult), (int)(pixel_height / dmult), System.Drawing.Imaging.PixelFormat.Format24bppRgb);
      using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage((System.Drawing.Image)b))
      {
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(bitmap, 0, 0, b.Width, b.Height);

        try
        {
          // OpenCV / cascade classifier loading is encapsulated by
          // IFaceDetector; the classifier is reused across photos
          // instead of being re-parsed from the XML on every call.
          IReadOnlyList<System.Drawing.Rectangle> faces = _faceDetector != null
            ? _faceDetector.Detect(b)
            : Array.Empty<System.Drawing.Rectangle>();

          // Merge into the existing sidecar (if any) so an Orientation set by
          // tools/orientation, plus geocoding flags / Nominatim data, survive
          // the face re-detection. Only Faces and freshly-read EXIF geo are
          // (re)written here.
          FinfoData finfo = existing ?? new FinfoData();
          finfo.Faces = faces.ToArray();
          finfo.Latitude ??= _latitude;
          finfo.Longitude ??= _longitude;
          finfo.PlaceName ??= _placeName;

          _finfoStore.Write(_name, finfo);

          if (faces.Count != 0)
          {
            _faces = new List<PointF>();
            foreach (var f in faces)
              _faces.Add(new PointF((float)((f.Right + f.Left) * dmult / 2.0 - pixel_width / 2.0),
                                    (float)((f.Top + f.Bottom) * dmult / 2.0 - pixel_height / 2.0)));
          }

          if (_latitude != null && _longitude != null && string.IsNullOrEmpty(_placeName) && _geocoder != null)
          {
            double lat = _latitude.Value;
            double lon = _longitude.Value;
            string imgName = _name;
            var geocoder = _geocoder;
            var finfoStore = _finfoStore;

            Task.Run(async () =>
            {
              try
              {
                var result = await geocoder.ReverseGeocodeAsync(lat, lon);

                var data = finfoStore.Read(imgName);
                if (data != null)
                {
                  data.GeocodingAttempted = true;
                  if (result != null && !string.IsNullOrEmpty(result.PlaceName))
                  {
                    // Volatile.Write pairs with Volatile.Read in the description
                    // getter so the UI thread observes the new place name
                    // without needing a lock.
                    Volatile.Write(ref _placeName, result.PlaceName);
                    data.PlaceName = result.PlaceName;
                    data.NominatimData = result.FullResponse;
                  }
                  finfoStore.Write(imgName, data);
                }
              }
              catch (Exception ex)
              {
                Log.Error(ex, "Async geocoding failed for {Image}", imgName);
              }
            });
          }

          _processed = true;
        }
        catch (Exception ex)
        {
          // Without the file name + classifier source we previously had no
          // way to tell whether OpenCV native DLLs failed to load, the
          // resized Bitmap was rejected by BitmapConverter, or .finfo
          // writing collapsed on permissions.
          Log.Error(ex, "FindFaces failed for {Image} (detector present: {HasDetector})", _name, _faceDetector != null);
        }
      }
    }
  }

  private BitmapImage Bitmap2BitmapImage(System.Drawing.Bitmap bitmap)
  {
    BitmapImage bitmapImage = new BitmapImage();
    using (MemoryStream outStream = new MemoryStream())
    {
      bitmap.Save(outStream, System.Drawing.Imaging.ImageFormat.Bmp);
      outStream.Position = 0;
      bitmapImage.BeginInit();
      bitmapImage.StreamSource = outStream;
      bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
      bitmapImage.EndInit();
    }

    // Freeze for cross-thread safety: animation/property updates may touch
    // the BitmapImage from the dispatcher while it's also referenced by
    // background tasks. A frozen Freezable is allowed on any thread.
    bitmapImage.Freeze();
    return bitmapImage;
  }

}

