using System;
using System.IO;
using Serilog;
using Yaps.Core.Abstractions;
using Yaps.Core.Video;

namespace Yaps.Infrastructure.Video;

/// <summary>
/// File-backed <see cref="IVideoMetadataProvider"/>: opens the video and lets
/// <see cref="Mp4MetadataReader"/> walk its box headers. Sequential access is
/// off on purpose — the reader seeks (the moov box can sit after the media
/// data) — and the buffer is small because only headers are read.
/// </summary>
public sealed class Mp4VideoMetadataProvider : IVideoMetadataProvider
{
    public VideoMetadata? TryRead(string videoPath)
    {
        if (string.IsNullOrEmpty(videoPath))
            return null;

        try
        {
            using var stream = new FileStream(videoPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                                              bufferSize: 8 * 1024, FileOptions.RandomAccess);
            var metadata = Mp4MetadataReader.TryRead(stream);
            if (metadata is null)
                Log.Warning("Could not read video metadata from {Video}; it stays out of the rotation", videoPath);

            return metadata;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not open video {Video}", videoPath);
            return null;
        }
    }
}
