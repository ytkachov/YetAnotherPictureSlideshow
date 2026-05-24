using System.Collections.Generic;
using System.Drawing;
using System.Windows.Media.Imaging;
using Yaps.Core.Models;

namespace Yaps.Infrastructure.Images;

/// <summary>
/// Turns <see cref="ImageMetadata"/> into a ready-to-display frozen
/// <see cref="BitmapImage"/> + the per-photo face accents. Owns the
/// JPEG decode, the optional ONNX orientation detection and the Haar
/// face detection, persisting their outputs into the .finfo sidecar
/// via <see cref="Yaps.Core.Abstractions.IFinfoStore"/>. Synchronous
/// because callers (the prefetch worker and the on-UI fallback path)
/// want a finished result back, not a Task.
/// </summary>
public interface IImageBitmapLoader
{
    LoadedImage Load(ImageMetadata meta);
}

/// <summary>
/// Result of one <see cref="IImageBitmapLoader.Load"/> call. Accents
/// are in the loaded bitmap's pixel space, centred on the bitmap's
/// midpoint — same convention as the legacy <c>LocalImageInfo</c>
/// produced, so the Ken-Burns pan in <c>FrameViewModel</c> works
/// without translation.
/// </summary>
public sealed record LoadedImage(BitmapImage Bitmap, IReadOnlyList<PointF> Accents);
