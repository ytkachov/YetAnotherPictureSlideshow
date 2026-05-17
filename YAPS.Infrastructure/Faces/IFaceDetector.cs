using System.Collections.Generic;
using System.Drawing;

namespace Yaps.Infrastructure.Faces;

/// <summary>
/// Face detection contract. Lives in Infrastructure (not Core) because
/// the natural input type is System.Drawing.Bitmap, which would force a
/// Windows-only dependency onto the otherwise cross-platform Core.
/// </summary>
public interface IFaceDetector
{
    IReadOnlyList<Rectangle> Detect(Bitmap bitmap);
}
