using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using ExifLibrary;
using Serilog;
using Yaps.Core.Abstractions;
using Yaps.Core.Models;
using Yaps.Infrastructure.Images;

/// <summary>
/// Slideshow-side wrapper around <see cref="ImageMetadata"/>. Stage 4
/// split: the EXIF / .finfo state lives in <see cref="Meta"/>, the
/// JPEG decode + ONNX orientation + Haar face detection live behind
/// <see cref="IImageBitmapLoader"/>, and this class is the thin
/// adapter that lets the slideshow's <c>ImageInfo</c> contract keep
/// the same shape. The only state added here on top of the metadata
/// is the per-show accent cache (filled by the bitmap getter, read
/// back by accent_count/accent) and a guard so the fire-and-forget
/// reverse-geocoding task only runs once per photo per session.
/// </summary>
public class LocalImageInfo : ImageInfo
{
  public readonly ImageMetadata Meta;
  internal int _shown;
  internal List<string> _messages = new();

  private readonly IGeocoder _geocoder;
  private readonly IImageBitmapLoader _loader;
  private readonly IFinfoStore _finfoStore;

  private readonly object _metaLock = new();
  private volatile bool _metadataLoaded;
  private volatile bool _geocodingQueued;

  // Populated by the bitmap getter — accent_count / accent read this
  // back without going through the loader again. Volatile so the prefetch
  // worker can publish to the UI thread without a lock.
  private IReadOnlyList<PointF> _accents = Array.Empty<PointF>();

  // EXIF date helpers and the geocoder are optional so this class
  // stays constructable from one-off scripts that don't run the DI
  // container. Production paths (LocalImages) pass everything.
  public LocalImageInfo(string nm, string videoname = null, IGeocoder geocoder = null,
                        IImageBitmapLoader loader = null, IFinfoStore finfoStore = null)
  {
    Meta = new ImageMetadata(nm, videoname);
    _geocoder = geocoder;
    _loader = loader;
    _finfoStore = finfoStore ?? new FileFinfoStore();
  }

  // Backwards-compatible accessor for the existing field-style read
  // sites (LocalImages.Add / WriteStat / Composition).
  internal string _name => Meta.Path;
  internal DateTime? _dateTaken => Meta.DateTaken;

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

  // EXIF used to be read for every photo up front during the library
  // scan, which meant pulling every file over the network just for
  // metadata. The slideshow index only needs the file path, so the
  // read is deferred to just before display.
  private void ReadExif()
  {
    // A previous GeoTagger run may have flagged this file's EXIF as
    // unreadable; honour that and skip the (failing) read. The store
    // resolves the .finfo location (next to the photo, or a configured
    // finfo folder).
    var existing = _finfoStore.Read(Meta.Path);

    // A corrected orientation in .finfo (written by tools/orientation)
    // wins over EXIF: this library's EXIF Orientation is always
    // Normal/absent even for visibly sideways photos, so the sidecar
    // is the authoritative source. Applied before the early return so
    // it survives even when EXIF is skipped.
    bool orientationFromFinfo = existing?.Orientation is int finfoOrientation
                                && finfoOrientation is >= 1 and <= 8;
    if (orientationFromFinfo)
      Meta.Orientation = (ushort)existing!.Orientation!.Value;

    Meta.OrientationDetectionAttempted = existing?.OrientationDetectionAttempted == true || orientationFromFinfo;

    if (existing != null && existing.ExifReadFailed)
    {
      Meta.ExifReadFailed = true;
      return;
    }

    try
    {
      var reader = ImageFile.FromFile(Meta.Path);

      // Fall back to the EXIF Orientation tag only when .finfo did not
      // specify one. ExifLibrary exposes it as ExifEnumProperty<Orientation>,
      // not ExifUShort, so the old Get<ExifUShort> always returned null
      // and rotation was never applied; the enum's numeric value is the
      // code 1-8.
      if (!orientationFromFinfo &&
          reader.Properties[ExifTag.Orientation]?.Value is Orientation exifOrientation)
        Meta.Orientation = (ushort)exifOrientation;

      // Prefer the capture time (DateTimeOriginal) over the file-edit
      // time (DateTime): re-saved / exported photos carry an edit date
      // that isn't when the shot was taken. Each candidate is validated
      // because some files store 0001-01-01 in DateTimeOriginal.
      Meta.DateTaken =
          ReadValidDate(reader, ExifTag.DateTimeOriginal) ??
          ReadValidDate(reader, ExifTag.DateTimeDigitized) ??
          ReadValidDate(reader, ExifTag.DateTime) ??
          FileTimestampFallback(Meta.Path);

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
            Meta.Latitude = lat;
            Meta.Longitude = lon;
          }
        }
      }
      catch (Exception ex2)
      {
        Log.Error(ex2, "GPS EXIF failed for {Image}", Meta.Path);
      }

      // PlaceName cached in the sidecar wins over an empty in-memory
      // value (description binding wants it on the first show, before
      // any background geocoder result has landed).
      if (existing != null)
        Meta.PlaceName = existing.PlaceName;
    }
    catch (Exception ex)
    {
      Log.Error(ex, "Image: {Image}", Meta.Path);
      _messages.Add("Exeption " + ex.ToString());
    }
  }

  private static DateTime? ReadValidDate(ImageFile reader, ExifTag tag)
  {
    var prop = reader.Properties.Get<ExifDateTime>(tag);
    if (prop == null)
      return null;

    DateTime value = prop;
    return value.Year > 1 ? value : null;
  }

  private static DateTime? FileTimestampFallback(string path)
  {
    try
    {
      var write = File.GetLastWriteTime(path);
      var create = File.GetCreationTime(path);
      var earliest = write < create ? write : create;
      return earliest.Year > 1 ? earliest : null;
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
      return Meta.Orientation switch
      {
        2 => RotateFlipType.RotateNoneFlipX,
        3 => RotateFlipType.Rotate180FlipNone,
        4 => RotateFlipType.RotateNoneFlipY,
        5 => RotateFlipType.Rotate90FlipX,
        6 => RotateFlipType.Rotate90FlipNone,
        7 => RotateFlipType.Rotate270FlipX,
        8 => RotateFlipType.Rotate270FlipNone,
        _ => RotateFlipType.RotateNoneFlipNone,
      };
    }
  }

  public bool has_accompanying_video => Meta.HasAccompanyingVideo;
  public string video_name => Meta.VideoPath;

  public string description
  {
    get
    {
      string place = Meta.PlaceName;
      string d = Meta.DateTaken?.ToString("dd/MM/yyyy") ?? "";
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
      var loaded = _loader.Load(Meta);
      Volatile.Write(ref _accents, loaded.Accents);
      MaybeQueueGeocoding();
      return loaded.Bitmap;
    }
  }

  public int accent_count => Volatile.Read(ref _accents).Count;

  public PointF accent
  {
    get
    {
      var faces = Volatile.Read(ref _accents);
      if (faces.Count == 0)
        return new PointF(-1.0F, -1.0F);

      int acc = Random.Shared.Next(faces.Count);
      return faces[acc];
    }
  }

  // Reverse-geocoding stays fire-and-forget: the result lands on the
  // .finfo via IFinfoStore and on Meta.PlaceName so the next description
  // read picks it up. Guarded so we only queue once per LocalImageInfo
  // (avoids hammering Nominatim if the same photo cycles through).
  private void MaybeQueueGeocoding()
  {
    if (_geocoder == null)
      return;
    if (_geocodingQueued)
      return;
    if (Meta.Latitude == null || Meta.Longitude == null)
      return;
    if (!string.IsNullOrEmpty(Meta.PlaceName))
      return;

    FinfoData existing;
    try
    {
      existing = _finfoStore.Read(Meta.Path);
    }
    catch
    {
      existing = null;
    }
    if (existing?.GeocodingAttempted == true)
    {
      _geocodingQueued = true;
      return;
    }

    _geocodingQueued = true;
    double lat = Meta.Latitude.Value;
    double lon = Meta.Longitude.Value;
    string imgName = Meta.Path;
    var geocoder = _geocoder;
    var finfoStore = _finfoStore;
    var meta = Meta;

    Task.Run(async () =>
    {
      try
      {
        var result = await geocoder.ReverseGeocodeAsync(lat, lon);

        var data = finfoStore.Read(imgName);
        if (data == null)
          return;

        data.GeocodingAttempted = true;
        if (result != null && !string.IsNullOrEmpty(result.PlaceName))
        {
          meta.PlaceName = result.PlaceName;
          data.PlaceName = result.PlaceName;
          data.NominatimData = result.FullResponse;
        }
        finfoStore.Write(imgName, data);
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Async geocoding failed for {Image}", imgName);
      }
    });
  }
}
