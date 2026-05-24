using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;
using Serilog;
using Yaps.Core.Abstractions;
using Yaps.Core.Models;
using Yaps.Infrastructure.Faces;
using Yaps.Infrastructure.Orientation;

namespace Yaps.Infrastructure.Images;

/// <summary>
/// Reference WPF implementation of <see cref="IImageBitmapLoader"/>.
/// Owns the bitmap pipeline that used to live inside
/// <c>LocalImageInfo.bitmap</c>: optional live ONNX orientation
/// detection, EXIF rotation, Haar face detection, and persisting both
/// outputs into the .finfo sidecar via <see cref="IFinfoStore"/>.
///
/// Stage 6.7b adds <c>DecodePixelWidth</c> support — display bitmaps
/// are decoded at screen width (via the constructor hint) so a 4K JPEG
/// shown on a 1080p frame doesn't retain a 24 MB pixel buffer. Face
/// detection still runs against the full-res rotated GDI bitmap on the
/// slow path so cached .finfo rectangles stay meaningful across decode
/// hints; accents are then scaled into the display bitmap's pixel
/// space so <c>FrameViewModel.SetImage</c> needs no change.
///
/// Stateless across calls — the only mutable state is the underlying
/// <see cref="Yaps.Infrastructure.Faces.IFaceDetector"/>'s cascade and
/// the ONNX session. The slideshow's prefetch design serialises loads
/// (one in flight at any time), so concurrent access doesn't happen.
/// </summary>
public sealed class WpfImageBitmapLoader : IImageBitmapLoader
{
    // Down-scale factor used during face detection so OpenCV runs on a
    // smaller bitmap; multiplied back when face rects are turned into
    // bitmap-space PointF accents. Must match the inverse multiplier
    // used here AND in the legacy code that wrote .finfo so cached
    // rectangles keep their meaning.
    private const double FaceDetectionDownscale = 3.0;

    private static readonly HashSet<int> ActionableOrientationCodes = new() { 6, 8 };
    private const double OrientationMinConfidence = 0.5;

    private readonly IFaceDetector _faceDetector;
    private readonly IOrientationDetector? _orientationDetector;
    private readonly IFinfoStore _finfoStore;

    // 0 = no decode hint (always full-res). Otherwise the target display
    // width in source pixels; capped against the actual source width so
    // we never upscale.
    private readonly int _decodePixelWidth;

    public WpfImageBitmapLoader(IFaceDetector faceDetector, IOrientationDetector? orientationDetector, IFinfoStore finfoStore)
        : this(faceDetector, orientationDetector, finfoStore, decodePixelWidth: 0) { }

    public WpfImageBitmapLoader(IFaceDetector faceDetector, IOrientationDetector? orientationDetector, IFinfoStore finfoStore, int decodePixelWidth)
    {
        _faceDetector = faceDetector;
        _orientationDetector = orientationDetector;
        _finfoStore = finfoStore;
        _decodePixelWidth = decodePixelWidth;
    }

    public LoadedImage Load(ImageMetadata meta)
    {
        var existing = TryReadFinfo(meta.Path);

        // Live orientation detection runs when the photo has no recorded
        // orientation AND the model wasn't already evaluated for it.
        bool needsOrientationDetection = _orientationDetector != null
                                         && meta.Orientation == 0
                                         && !meta.OrientationDetectionAttempted;

        var rf = ToRotateFlip(meta.Orientation);

        // Fast path: no rotation needed, the model isn't due, and faces
        // are already cached. Skip GDI entirely and only decode at the
        // requested display size.
        if (!needsOrientationDetection
            && rf == RotateFlipType.RotateNoneFlipNone
            && existing?.Faces != null)
        {
            return LoadFast(meta, existing.Faces);
        }

        return LoadSlow(meta, existing, needsOrientationDetection);
    }

    private LoadedImage LoadFast(ImageMetadata meta, Rectangle[] faces)
    {
        // Source dimensions come from the JPEG header so we can clip the
        // DecodePixelWidth hint without upscaling, and so accents can be
        // scaled into the display bitmap's actual pixel space. Header
        // parse is cheap; we never trigger a full decode here.
        var (srcW, srcH) = ReadSourceDimensions(meta.Path);

        var displayBmp = LoadDisplayBitmap(meta.Path, srcW);

        // Fast path is gated on orientation==None so rotated dims equal
        // source dims, which is what the cached rectangles were detected
        // against.
        var accents = ScaleFacesToDisplay(faces, srcW, srcH, displayBmp.PixelWidth, displayBmp.PixelHeight);
        return new LoadedImage(displayBmp, accents);
    }

    private LoadedImage LoadSlow(ImageMetadata meta, FinfoData? existing, bool needsOrientationDetection)
    {
        // Full-res decode for detection — keeps cached .finfo rectangles
        // meaningful no matter what _decodePixelWidth the user picks.
        var sourceBmp = new BitmapImage(new Uri(meta.Path));
        using var outStream = new MemoryStream();
        BitmapEncoder enc = new BmpBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(sourceBmp));
        enc.Save(outStream);

        using var bitmap = new Bitmap(outStream);

        if (needsOrientationDetection)
        {
            DetectAndPersistOrientation(meta, bitmap, ref existing);
        }
        var rf = ToRotateFlip(meta.Orientation);
        bitmap.RotateFlip(rf);

        // Display bitmap is the rotated GDI pixels re-decoded by WPF at
        // the requested width. The detection passes above ran on the
        // un-downsampled rotated pixels so accents stay accurate even
        // when the display copy is smaller.
        var displayBmp = Bitmap2BitmapImage(bitmap, _decodePixelWidth);

        var accents = ResolveAccentsSlow(meta, bitmap, displayBmp, existing);
        return new LoadedImage(displayBmp, accents);
    }

    private IReadOnlyList<PointF> ResolveAccentsSlow(ImageMetadata meta, Bitmap rotated, BitmapImage display, FinfoData? existing)
    {
        int rotatedWidth = rotated.Width;
        int rotatedHeight = rotated.Height;

        if (existing?.Faces != null)
            return ScaleFacesToDisplay(existing.Faces, rotatedWidth, rotatedHeight, display.PixelWidth, display.PixelHeight);

        using var b = new Bitmap((int)(rotatedWidth / FaceDetectionDownscale),
                                 (int)(rotatedHeight / FaceDetectionDownscale),
                                 System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var g = System.Drawing.Graphics.FromImage(b))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(rotated, 0, 0, b.Width, b.Height);
        }

        IReadOnlyList<Rectangle> faces;
        try
        {
            faces = _faceDetector.Detect(b);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "FindFaces failed for {Image}", meta.Path);
            return Array.Empty<PointF>();
        }

        // Merge into the existing sidecar (if any) so Orientation /
        // geocoding flags / Nominatim data survive face re-detection.
        var finfo = existing ?? new FinfoData();
        finfo.Faces = new Rectangle[faces.Count];
        for (int i = 0; i < faces.Count; i++)
            finfo.Faces[i] = faces[i];
        finfo.Latitude ??= meta.Latitude;
        finfo.Longitude ??= meta.Longitude;
        finfo.PlaceName ??= meta.PlaceName;

        try
        {
            _finfoStore.Write(meta.Path, finfo);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to persist .finfo for {Image}", meta.Path);
        }

        return ScaleFacesToDisplay(finfo.Faces, rotatedWidth, rotatedHeight, display.PixelWidth, display.PixelHeight);
    }

    // Face Rectangle is in detection-bitmap coords (rotated / dmult).
    // Convert to display-pixel offset from the display bitmap's centre,
    // which is what FrameViewModel.SetImage divides through dc.
    // Rotated and display share the same aspect ratio so X and Y scale
    // by the same proportion.
    private static IReadOnlyList<PointF> ScaleFacesToDisplay(Rectangle[] faces, int rotatedWidth, int rotatedHeight, int displayWidth, int displayHeight)
    {
        if (faces == null || faces.Length == 0)
            return Array.Empty<PointF>();

        double scaleX = FaceDetectionDownscale * (double)displayWidth / rotatedWidth;
        double scaleY = FaceDetectionDownscale * (double)displayHeight / rotatedHeight;

        var accents = new PointF[faces.Length];
        for (int i = 0; i < faces.Length; i++)
        {
            var f = faces[i];
            accents[i] = new PointF(
                (float)((f.Left + f.Right) / 2.0 * scaleX - displayWidth / 2.0),
                (float)((f.Top + f.Bottom) / 2.0 * scaleY - displayHeight / 2.0));
        }
        return accents;
    }

    // Loads the on-screen bitmap from the URI directly, applying
    // DecodePixelWidth so WPF skips the full-resolution decode. Used by
    // the fast path; the slow path uses Bitmap2BitmapImage on the
    // already-rotated GDI bitmap.
    private BitmapImage LoadDisplayBitmap(string path, int sourceWidth)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        if (_decodePixelWidth > 0 && _decodePixelWidth < sourceWidth)
            bmp.DecodePixelWidth = _decodePixelWidth;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    // Cheap header-only parse via BitmapDecoder — JPEG SOF parsing
    // populates PixelWidth/PixelHeight before any pixel data is decoded.
    // Used only on the fast path; the slow path reads dimensions from
    // the already-loaded GDI bitmap.
    private static (int Width, int Height) ReadSourceDimensions(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(fs,
            BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache,
            BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }

    private FinfoData? TryReadFinfo(string path)
    {
        try
        {
            return _finfoStore.Read(path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load .finfo for {Image}", path);
            return null;
        }
    }

    private void DetectAndPersistOrientation(ImageMetadata meta, Bitmap pixels, ref FinfoData? existing)
    {
        try
        {
            var result = _orientationDetector!.Detect(pixels);
            bool actionable = ActionableOrientationCodes.Contains(result.Code)
                              && result.Confidence >= OrientationMinConfidence;

            if (actionable)
                meta.Orientation = (ushort)result.Code;
            meta.OrientationDetectionAttempted = true;

            var data = existing ?? new FinfoData();
            data.OrientationDetectionAttempted = true;
            if (actionable)
            {
                data.Orientation = result.Code;
                data.Faces = null;
            }
            _finfoStore.Write(meta.Path, data);
            existing = data;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Live orientation detection failed for {Image}", meta.Path);
            meta.OrientationDetectionAttempted = true;
        }
    }

    private static BitmapImage Bitmap2BitmapImage(Bitmap bitmap, int decodePixelWidth)
    {
        var bitmapImage = new BitmapImage();
        using (var outStream = new MemoryStream())
        {
            bitmap.Save(outStream, System.Drawing.Imaging.ImageFormat.Bmp);
            outStream.Position = 0;
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = outStream;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            if (decodePixelWidth > 0 && decodePixelWidth < bitmap.Width)
                bitmapImage.DecodePixelWidth = decodePixelWidth;
            bitmapImage.EndInit();
        }
        bitmapImage.Freeze();
        return bitmapImage;
    }

    private static RotateFlipType ToRotateFlip(ushort orientation) => orientation switch
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
