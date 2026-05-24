using System;
using System.Threading;

namespace Yaps.Core.Models;

/// <summary>
/// Persistent per-photo state — everything that gets read from EXIF
/// and the .finfo sidecar (no <c>BitmapImage</c>, no GDI). Stage 4
/// split: previously these fields lived inside the WPF-bound
/// <c>LocalImageInfo</c> and could not be reused from utilities like
/// GeoTagger without dragging in PresentationCore. The companion type
/// is <see cref="Yaps.Infrastructure.Images.IImageBitmapLoader"/>,
/// which turns one of these into a frozen <c>BitmapImage</c>.
/// </summary>
public sealed class ImageMetadata
{
    public string Path { get; }
    public string? VideoPath { get; }

    public DateTime? DateTaken { get; set; }

    // EXIF orientation code (1-8). 0 means "not yet known / never
    // recorded" so the live orientation detector knows to run.
    public ushort Orientation { get; set; }

    // Mirrors FinfoData.OrientationDetectionAttempted: true once the
    // ONNX model has been evaluated for this photo, so we don't keep
    // running it on every show even when no actionable rotation was
    // recorded.
    public bool OrientationDetectionAttempted { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // ExifReadFailed mirrors FinfoData; when true, EXIF reading has
    // already been tried and is known to fail (corrupt JPEG / unsupported
    // tag layout) so we don't retry on every metadata-load attempt.
    public bool ExifReadFailed { get; set; }

    // PlaceName is the only field written from a thread other than the
    // one that built the metadata — the fire-and-forget reverse-geocoding
    // task updates it. Wrapped in Volatile so the UI thread that polls
    // the description sees the publish without a lock.
    private string? _placeName;
    public string? PlaceName
    {
        get => Volatile.Read(ref _placeName);
        set => Volatile.Write(ref _placeName, value);
    }

    public bool HasAccompanyingVideo => !string.IsNullOrEmpty(VideoPath) && !string.IsNullOrEmpty(Path);

    public ImageMetadata(string path, string? videoPath = null)
    {
        Path = path;
        VideoPath = videoPath;
    }
}
