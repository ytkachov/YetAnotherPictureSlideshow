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
/// Stateless across calls — the only mutable state is the underlying
/// <see cref="CascadeClassifier"/> wrapped by <see cref="IFaceDetector"/>
/// and the ONNX <c>InferenceSession</c>. The slideshow's prefetch
/// design serialises loads (one in flight at any time) so concurrent
/// access to either detector doesn't happen by construction.
/// </summary>
public sealed class WpfImageBitmapLoader : IImageBitmapLoader
{
    // Down-scale factor used during face detection so OpenCV runs on a
    // smaller bitmap; multiplied back when face rects are turned into
    // bitmap-space PointF accents. Must match the inverse multiplier
    // used here AND in the legacy code that wrote .finfo so cached
    // rectangles keep their meaning.
    private const double FaceDetectionDownscale = 3.0;

    // Actionable EXIF codes for live detection — mirrors OrientationTagger's
    // policy (the model is unreliable on 180° = code 3, so it's recorded
    // as attempted-but-noop). Confidence floor mirrors the same default.
    private static readonly HashSet<int> ActionableOrientationCodes = new() { 6, 8 };
    private const double OrientationMinConfidence = 0.5;

    private readonly IFaceDetector _faceDetector;
    private readonly IOrientationDetector? _orientationDetector;
    private readonly IFinfoStore _finfoStore;

    public WpfImageBitmapLoader(IFaceDetector faceDetector, IOrientationDetector? orientationDetector, IFinfoStore finfoStore)
    {
        _faceDetector = faceDetector;
        _orientationDetector = orientationDetector;
        _finfoStore = finfoStore;
    }

    public LoadedImage Load(ImageMetadata meta)
    {
        var bmp_img = new BitmapImage(new Uri(meta.Path));

        // Live orientation detection runs when the photo has no recorded
        // orientation AND the model wasn't already evaluated for it. After
        // the backfill (Attempted=true across the library) this only fires
        // on new photos added since. Decided up front so the fast path
        // stays correct.
        bool needsOrientationDetection = _orientationDetector != null
                                         && meta.Orientation == 0
                                         && !meta.OrientationDetectionAttempted;

        var rf = ToRotateFlip(meta.Orientation);

        FinfoData? existing = TryReadFinfo(meta.Path);

        // Fast path: no rotation needed, the model isn't due, and faces
        // are already cached in .finfo. Skip GDI entirely.
        if (!needsOrientationDetection
            && rf == RotateFlipType.RotateNoneFlipNone
            && existing?.Faces != null)
        {
            bmp_img.Freeze();
            var fastAccents = FacesToAccents(existing.Faces, bmp_img.PixelWidth, bmp_img.PixelHeight);
            return new LoadedImage(bmp_img, fastAccents);
        }

        using var outStream = new MemoryStream();
        BitmapEncoder enc = new BmpBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp_img));
        enc.Save(outStream);

        // System.Drawing.Bitmap holds an unmanaged GDI handle — using
        // ensures it's freed even if face/orientation detection throws.
        using var bitmap = new Bitmap(outStream);

        // Detect on the loaded-but-not-yet-rotated pixels, so the result
        // matches the standard EXIF orientation contract. Must happen
        // BEFORE RotateFlip and BEFORE face detection (the latter runs on
        // the rotated bitmap and would otherwise cache faces in the
        // wrong frame).
        if (needsOrientationDetection)
        {
            DetectAndPersistOrientation(meta, bitmap, ref existing);
            rf = ToRotateFlip(meta.Orientation);
        }

        bitmap.RotateFlip(rf);

        var bitmapImage = Bitmap2BitmapImage(bitmap);
        var accents = ResolveFaces(meta, bitmap, existing);

        return new LoadedImage(bitmapImage, accents);
    }

    private FinfoData? TryReadFinfo(string path)
    {
        try
        {
            // FileFinfoStore swallows JSON errors internally; we still
            // guard here because an unexpected exception escaping would
            // disappear the photo from the slideshow rotation.
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
                // Cached faces were detected against the un-rotated frame
                // and would land in the wrong place once the screensaver
                // rotates the photo. Drop them; the face pass below will
                // recompute on the rotated bitmap and write them back.
                data.Faces = null;
            }
            _finfoStore.Write(meta.Path, data);
            existing = data;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Live orientation detection failed for {Image}", meta.Path);
            // Still mark attempted in-memory so we don't keep trying on
            // every show of the same photo in this process.
            meta.OrientationDetectionAttempted = true;
        }
    }

    private IReadOnlyList<PointF> ResolveFaces(ImageMetadata meta, Bitmap rotated, FinfoData? existing)
    {
        int pixel_width = rotated.Width;
        int pixel_height = rotated.Height;

        if (existing?.Faces != null)
            return FacesToAccents(existing.Faces, pixel_width, pixel_height);

        // Down-scale, detect on the small bitmap, then translate the rects
        // back into the rotated bitmap's coordinate space via dmult.
        using var b = new Bitmap((int)(pixel_width / FaceDetectionDownscale),
                                 (int)(pixel_height / FaceDetectionDownscale),
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

        // Merge into the existing sidecar (if any) so an Orientation set
        // by tools/orientation plus geocoding flags / Nominatim data
        // survive the face re-detection. Only Faces and freshly-read
        // EXIF geo are (re)written here.
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

        return FacesToAccents(finfo.Faces, pixel_width, pixel_height);
    }

    private static IReadOnlyList<PointF> FacesToAccents(Rectangle[] faces, int pixel_width, int pixel_height)
    {
        if (faces == null || faces.Length == 0)
            return Array.Empty<PointF>();

        var accents = new PointF[faces.Length];
        for (int i = 0; i < faces.Length; i++)
        {
            var f = faces[i];
            accents[i] = new PointF(
                (float)((f.Right + f.Left) * FaceDetectionDownscale / 2.0 - pixel_width / 2.0),
                (float)((f.Top + f.Bottom) * FaceDetectionDownscale / 2.0 - pixel_height / 2.0));
        }
        return accents;
    }

    private static BitmapImage Bitmap2BitmapImage(Bitmap bitmap)
    {
        var bitmapImage = new BitmapImage();
        using (var outStream = new MemoryStream())
        {
            bitmap.Save(outStream, System.Drawing.Imaging.ImageFormat.Bmp);
            outStream.Position = 0;
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = outStream;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
        }
        // Frozen Freezables are allowed on any thread, which lets the
        // prefetch worker hand the result to the UI thread without a
        // marshal step.
        bitmapImage.Freeze();
        return bitmapImage;
    }

    // Mirrors the legacy LocalImageInfo orientation getter exactly so
    // the rotated bitmap matches what FrameViewModel expects.
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
