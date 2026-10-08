namespace Yaps.Core.Selection;

/// <summary>
/// How one folder takes its turn in <see cref="PhotoRotation"/>.
/// </summary>
/// <param name="BatchSize">
/// Items handed out per visit. Photo folders use the configured
/// PhotosPerFolder; the virtual video folder uses 1, because a visit there
/// means "play one clip, then move on to the next folder".
/// </param>
/// <param name="WeightMultiplier">
/// How many times the folder's fair share of visits is dealt into the folder
/// deck. 1 is the fair share — the folder comes up in proportion to how many
/// items it holds. Above 1 the folder is favoured on purpose: a library of
/// 22 000 photos and a couple of minutes of video would otherwise show a clip
/// about once every five hours. 0 keeps the folder out of the rotation
/// entirely, which is how video is switched off.
/// </param>
public sealed record FolderRotationPolicy(int BatchSize, int WeightMultiplier);
