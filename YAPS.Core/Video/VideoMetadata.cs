using System;

namespace Yaps.Core.Video;

/// <summary>
/// What the slideshow needs to know about a video file before it can place it
/// in the rotation: how long it runs, how it has to be rotated for display,
/// and when it was recorded (used as the on-screen caption, same as a photo's
/// EXIF date).
/// </summary>
/// <param name="Duration">Total playing time.</param>
/// <param name="RotationDegrees">
/// Clockwise rotation to apply for correct display (0, 90, 180 or 270), taken
/// from the track's transformation matrix. Phone recordings are commonly
/// stored sideways with the rotation left in the matrix.
/// </param>
/// <param name="CreatedUtc">
/// Container creation time, or null when the file doesn't carry a plausible
/// one (plenty of muxers leave it at zero).
/// </param>
/// <param name="IsLivePhoto">
/// The file is the movie half of an iPhone Live Photo (a 1.5-3 s clip around
/// a still), not a recording in its own right — even when its still is
/// missing from the folder.
/// </param>
/// <param name="StillImageTime">
/// For a Live Photo movie: the moment the still was taken, from the
/// <c>com.apple.quicktime.still-image-time</c> metadata track. The still is
/// taken mid-movie, not at its end, so this is where playback has to stop for
/// the last frame shown to be the photo. Null when the file doesn't say.
/// </param>
public sealed record VideoMetadata(TimeSpan Duration, int RotationDegrees, DateTime? CreatedUtc,
                                   bool IsLivePhoto = false, TimeSpan? StillImageTime = null);
