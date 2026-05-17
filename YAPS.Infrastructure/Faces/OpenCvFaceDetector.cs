using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using CvSize = OpenCvSharp.Size;
using DrawingRectangle = System.Drawing.Rectangle;

namespace Yaps.Infrastructure.Faces;

/// <summary>
/// OpenCV / Haar-cascade face detector. The CascadeClassifier is loaded
/// once and reused — the previous static FaceDetection.DetectFace.Detect
/// constructed a fresh classifier per image, paying disk+parse cost on
/// every slideshow tick that produced an uncached photo.
/// </summary>
public sealed class OpenCvFaceDetector : IFaceDetector, IDisposable
{
    private readonly CascadeClassifier _classifier;
    private bool _disposed;

    public OpenCvFaceDetector(string cascadeXmlPath)
    {
        if (string.IsNullOrEmpty(cascadeXmlPath))
            throw new ArgumentException("Cascade XML path is required", nameof(cascadeXmlPath));
        if (!File.Exists(cascadeXmlPath))
            throw new FileNotFoundException("Haar cascade XML not found", cascadeXmlPath);

        _classifier = new CascadeClassifier(cascadeXmlPath);
    }

    public IReadOnlyList<DrawingRectangle> Detect(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        using var mat = BitmapConverter.ToMat(bitmap);
        using var gray = new Mat();
        Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.EqualizeHist(gray, gray);

        var detected = _classifier.DetectMultiScale(gray, 1.1, 10, (HaarDetectionTypes)0, new CvSize(20, 20));
        return detected.Select(r => new DrawingRectangle(r.X, r.Y, r.Width, r.Height)).ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _classifier.Dispose();
        GC.SuppressFinalize(this);
    }
}
