using System;
using System.Collections.Generic;

namespace Yaps.Core.Video;

/// <summary>
/// Splits a video's running time into the slices the rotation deals out.
/// </summary>
public static class VideoChunking
{
    /// <summary>
    /// Cuts <paramref name="duration"/> into chunks of <paramref name="chunkLength"/>.
    /// A trailing piece shorter than <paramref name="minChunkLength"/> is folded
    /// into the previous chunk rather than shown on its own — a two-second
    /// fragment of a video reads as a glitch, not as a clip.
    /// </summary>
    public static IReadOnlyList<(TimeSpan Start, TimeSpan Length)> Split(
        TimeSpan duration, TimeSpan chunkLength, TimeSpan minChunkLength)
    {
        if (duration <= TimeSpan.Zero)
            return Array.Empty<(TimeSpan, TimeSpan)>();

        if (chunkLength <= TimeSpan.Zero || duration <= chunkLength + minChunkLength)
            return new[] { (TimeSpan.Zero, duration) };

        var chunks = new List<(TimeSpan Start, TimeSpan Length)>();
        TimeSpan at = TimeSpan.Zero;
        while (at < duration)
        {
            TimeSpan remaining = duration - at;
            if (remaining <= chunkLength + minChunkLength)
            {
                // Last piece: take all of it, so the tail is never a flash and
                // never longer than chunk + min.
                chunks.Add((at, remaining));
                break;
            }

            chunks.Add((at, chunkLength));
            at += chunkLength;
        }

        return chunks;
    }
}
