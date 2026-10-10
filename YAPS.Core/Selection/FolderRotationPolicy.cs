namespace Yaps.Core.Selection;

/// <summary>
/// How one folder takes its turn in <see cref="PhotoRotation"/>.
/// </summary>
/// <param name="BatchSize">
/// Items handed out per visit. Photo folders use the configured
/// PhotosPerFolder; the virtual video folder uses 1, because a visit there
/// means "play one clip, then move on to the next folder".
/// </param>
/// <param name="VisitsPerPass">
/// Null (photo folders): the folder comes up once per batch it needs to be
/// fully covered, so one pass shows each of its items exactly once. A number
/// fixes the folder's visits per pass instead, whatever it holds — how the
/// long-form video folder gets a set share of screen time: thousands of video
/// slices dealt one visit each would otherwise crowd the photos out. Its deck
/// then simply carries on across passes. 0 keeps the folder out entirely.
/// </param>
public sealed record FolderRotationPolicy(int BatchSize, int? VisitsPerPass = null);
