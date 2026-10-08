using System;

namespace Yaps.Core.Video;

/// <summary>
/// One slice of a video file, as handed to the slideshow frame: play
/// <see cref="Path"/> from <see cref="Start"/> for <see cref="Length"/>,
/// rotated by <see cref="RotationDegrees"/>.
///
/// A clip — not a whole file — is the unit of rotation, so a long recording
/// takes its turn in pieces instead of holding the frame for minutes.
/// </summary>
public sealed record VideoClip(string Path, TimeSpan Start, TimeSpan Length, int RotationDegrees);
