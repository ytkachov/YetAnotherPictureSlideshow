using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Yaps.Core.Video;

/// <summary>
/// Minimal ISO base-media (MP4 / M4V / QuickTime MOV) header reader: pulls
/// duration, display rotation and creation time out of the <c>moov</c> box.
///
/// Why parse by hand instead of asking a media API: the duration has to be
/// known during the library scan, on a background thread, before anything is
/// on screen — WPF's MediaElement only reports NaturalDuration once it has
/// opened the media on the UI thread, and an external probe (ffprobe) isn't
/// installed on the photo frame. This walks box headers only and seeks past
/// each payload, so the media data itself is never read: a handful of small
/// reads per file, which matters when the library sits on an SMB share.
///
/// Pure stream work, no file-system knowledge — that lives in Infrastructure.
/// </summary>
public static class Mp4MetadataReader
{
    private static readonly DateTime Epoch1904 = new(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // A creation time outside this range is a muxer artefact, not a date.
    private static readonly DateTime PlausibleFrom = new(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PlausibleTo = new(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Reads what we need, or returns null when the stream isn't an ISO
    /// base-media file, is truncated, or carries no usable duration. Never
    /// throws on malformed input — one bad file must not take the scan down.
    /// </summary>
    public static VideoMetadata? TryRead(Stream stream)
    {
        if (stream is null || !stream.CanSeek)
            return null;

        try
        {
            long end = stream.Length;
            if (FindBox(stream, 0, end, "moov") is not { } moov)
                return null;

            if (FindBox(stream, moov.PayloadStart, moov.PayloadEnd, "mvhd") is not { } mvhd)
                return null;

            if (!TryReadMovieHeader(stream, mvhd, out TimeSpan duration, out DateTime? created))
                return null;

            int rotation = ReadRotation(stream, moov);
            return new VideoMetadata(duration, rotation, created);
        }
        catch (Exception)
        {
            // IOException, EndOfStreamException, a nonsense box size — they all
            // mean the same thing to the caller: this file can't be placed in
            // the rotation. The caller logs it.
            return null;
        }
    }

    private static bool TryReadMovieHeader(Stream stream, Box mvhd, out TimeSpan duration, out DateTime? created)
    {
        duration = default;
        created = null;

        stream.Position = mvhd.PayloadStart;
        int version = ReadByte(stream);
        Skip(stream, 3); // flags

        ulong creationSeconds;
        uint timescale;
        ulong units;
        if (version == 1)
        {
            creationSeconds = ReadU64(stream);
            Skip(stream, 8); // modification time
            timescale = ReadU32(stream);
            units = ReadU64(stream);
        }
        else
        {
            creationSeconds = ReadU32(stream);
            Skip(stream, 4); // modification time
            timescale = ReadU32(stream);
            units = ReadU32(stream);
        }

        if (timescale == 0 || units == 0)
            return false;

        double seconds = (double)units / timescale;
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0 || seconds > 24 * 3600)
            return false;

        duration = TimeSpan.FromSeconds(seconds);
        created = ToDateTime(creationSeconds);
        return true;
    }

    private static DateTime? ToDateTime(ulong secondsSince1904)
    {
        if (secondsSince1904 == 0 || secondsSince1904 > 1_000_000_000_000)
            return null;

        var value = Epoch1904.AddSeconds(secondsSince1904);
        return value >= PlausibleFrom && value <= PlausibleTo ? value : null;
    }

    // Rotation lives in the video track's 3x3 transformation matrix. Audio and
    // metadata tracks carry the same box, so the video track is identified by a
    // non-zero display size.
    private static int ReadRotation(Stream stream, Box moov)
    {
        long cursor = moov.PayloadStart;
        while (cursor < moov.PayloadEnd)
        {
            stream.Position = cursor;
            if (!TryReadHeader(stream, moov.PayloadEnd, out Box trak))
                break;

            if (trak.PayloadEnd <= cursor)
                break;
            cursor = trak.PayloadEnd;

            if (trak.Type != "trak")
                continue;

            if (FindBox(stream, trak.PayloadStart, trak.PayloadEnd, "tkhd") is not { } tkhd)
                continue;

            if (TryReadTrackRotation(stream, tkhd, out int rotation))
                return rotation;
        }

        return 0;
    }

    private static bool TryReadTrackRotation(Stream stream, Box tkhd, out int rotation)
    {
        rotation = 0;

        stream.Position = tkhd.PayloadStart;
        int version = ReadByte(stream);
        Skip(stream, 3);                       // flags
        Skip(stream, version == 1 ? 32 : 20);  // creation, modification, track id, reserved, duration
        Skip(stream, 16);                      // reserved, layer, alternate group, volume, reserved

        // matrix: a b u / c d v / x y w. a and b are 16.16 fixed point and are
        // all the rotation needs.
        int a = ReadI32(stream);
        int b = ReadI32(stream);
        Skip(stream, 28);                      // rest of the matrix
        uint width = ReadU32(stream);
        uint height = ReadU32(stream);

        if (width == 0 || height == 0)
            return false;                      // not the video track

        if (a == 0 && b == 0)
            return false;                      // degenerate matrix, nothing to derive

        double angle = Math.Atan2(b / 65536.0, a / 65536.0) * 180.0 / Math.PI;
        if (double.IsNaN(angle))
            return false;

        // Snap to the quarter turn the display can actually apply; anything
        // else is a skew we don't support and is better left unrotated.
        int snapped = ((int)Math.Round(angle / 90.0) * 90) % 360;
        if (snapped < 0)
            snapped += 360;

        rotation = snapped;
        return true;
    }

    private readonly record struct Box(string Type, long PayloadStart, long PayloadEnd);

    private static Box? FindBox(Stream stream, long start, long end, string type)
    {
        long cursor = start;
        while (cursor < end)
        {
            stream.Position = cursor;
            if (!TryReadHeader(stream, end, out Box box))
                return null;

            if (box.Type == type)
                return box;

            if (box.PayloadEnd <= cursor)
                return null; // zero-length box: malformed, and would loop forever

            cursor = box.PayloadEnd;
        }

        return null;
    }

    private static bool TryReadHeader(Stream stream, long limit, out Box box)
    {
        box = default;

        long headerStart = stream.Position;
        if (headerStart + 8 > limit)
            return false;

        Span<byte> header = stackalloc byte[8];
        stream.ReadExactly(header);

        long size = BinaryPrimitives.ReadUInt32BigEndian(header);
        string type = Encoding.ASCII.GetString(header.Slice(4, 4));
        long headerSize = 8;

        if (size == 1)
        {
            // 64-bit size: large mdat boxes use this form.
            Span<byte> large = stackalloc byte[8];
            stream.ReadExactly(large);
            ulong big = BinaryPrimitives.ReadUInt64BigEndian(large);
            if (big > long.MaxValue)
                return false;
            size = (long)big;
            headerSize = 16;
        }
        else if (size == 0)
        {
            // Box runs to the end of its container.
            size = limit - headerStart;
        }

        if (size < headerSize)
            return false;

        long payloadEnd = headerStart + size;
        box = new Box(type, headerStart + headerSize, Math.Min(payloadEnd, limit));
        return true;
    }

    private static int ReadByte(Stream stream)
    {
        int b = stream.ReadByte();
        if (b < 0)
            throw new EndOfStreamException();
        return b;
    }

    private static void Skip(Stream stream, int count) => stream.Position += count;

    private static uint ReadU32(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt32BigEndian(buffer);
    }

    private static int ReadI32(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadInt32BigEndian(buffer);
    }

    private static ulong ReadU64(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[8];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt64BigEndian(buffer);
    }
}
