using System.Drawing;

namespace Yaps.Infrastructure.Orientation;

/// <summary>
/// Predicts the upright rotation of a photo from its pixels. Returns the
/// EXIF orientation code (1/3/6/8) that, applied by a standard viewer,
/// would restore the image to upright, plus the model's softmax confidence
/// in [0,1].
///
/// Lives in Infrastructure (not Core) because the natural input type is
/// <see cref="Bitmap"/>, which would force a Windows-only dependency onto
/// the otherwise cross-platform Core.
/// </summary>
public interface IOrientationDetector
{
    OrientationDetection Detect(Bitmap bitmap);
}

/// <summary>
/// Standard EXIF orientation code (1 = upright, 3 = 180, 6 = 90 CW, 8 = 90 CCW)
/// plus the model's confidence in the winning class.
/// </summary>
public readonly record struct OrientationDetection(int Code, double Confidence);
