using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;

namespace Yaps.Core.Video;

/// <summary>
/// Which files the library scan treats as video. Deliberately limited to the
/// ISO base-media family: those are what <see cref="Mp4MetadataReader"/> can
/// measure, and a video whose duration we can't measure can't be split into
/// chunks or given a fair share of the rotation.
/// </summary>
public static class VideoFileTypes
{
    private static readonly FrozenSet<string> Extensions =
        new[] { ".mp4", ".m4v", ".mov" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public static bool IsVideo(string path)
        => !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));
}
