using Yaps.Core.Video;

namespace Yaps.Core.Abstractions;

/// <summary>
/// Reads a video file's duration / rotation / creation time. The seam exists
/// so the library scan can measure videos without knowing how (today an
/// in-process MP4 header parse; a container we can't parse would need
/// something else) and so the parsing can be exercised without a scan.
/// </summary>
public interface IVideoMetadataProvider
{
    /// <summary>
    /// Metadata for the file, or null when it can't be measured — unreadable,
    /// unsupported container, or no usable duration. Implementations log and
    /// return null rather than throwing.
    /// </summary>
    VideoMetadata? TryRead(string videoPath);
}
