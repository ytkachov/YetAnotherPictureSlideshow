using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Yaps.Core.Models;

/// <summary>
/// One photo-folder paired with the folder its .finfo sidecars are stored in.
/// <see cref="FinfoFolder"/> empty means "store the sidecar next to the photo"
/// (the legacy behaviour); a non-empty path means the sidecar is stored ONLY
/// under that folder, mirroring the photo's relative subtree.
/// </summary>
public sealed record FinfoFolderPair(string PhotoFolder, string FinfoFolder);

/// <summary>
/// Configuration for <c>FileFinfoStore</c>: the positional pairing of the
/// <c>ImageFolder</c> and <c>FinfoFolder</c> registry lists. Built once from the
/// registry (see Infrastructure's RegistryConfig) and shared by the screensaver
/// and the utilities so they all resolve a photo's .finfo to the same location.
/// </summary>
public sealed class FinfoStoreOptions
{
    public static readonly FinfoStoreOptions Empty = new(Array.Empty<FinfoFolderPair>());

    public IReadOnlyList<FinfoFolderPair> Pairs { get; }

    public FinfoStoreOptions(IReadOnlyList<FinfoFolderPair> pairs)
    {
        Pairs = pairs ?? Array.Empty<FinfoFolderPair>();
    }

    /// <summary>
    /// Parses the two semicolon-separated registry lists into positional pairs.
    /// Both lists are split WITHOUT discarding empty entries so position i in
    /// <paramref name="finfoFolder"/> lines up with position i in
    /// <paramref name="imageFolder"/>. Photo entries have their trailing
    /// <c>\*</c> recursion marker and trailing separators stripped (the same
    /// base the slideshow scans). An empty / missing finfo entry pairs to
    /// "next to the photo".
    /// </summary>
    public static FinfoStoreOptions FromConfig(string? imageFolder, string? finfoFolder)
    {
        if (string.IsNullOrWhiteSpace(imageFolder))
            return Empty;

        var photos = imageFolder.Split(';');
        var finfos = (finfoFolder ?? string.Empty).Split(';');

        var pairs = new List<FinfoFolderPair>(photos.Length);
        for (int i = 0; i < photos.Length; i++)
        {
            string photo = NormalizePhotoFolder(photos[i]);
            if (photo.Length == 0)
                continue;

            string finfo = i < finfos.Length ? NormalizeFolder(finfos[i]) : string.Empty;
            pairs.Add(new FinfoFolderPair(photo, finfo));
        }

        return pairs.Count == 0 ? Empty : new FinfoStoreOptions(pairs);
    }

    private static string NormalizePhotoFolder(string raw)
    {
        string p = raw.Trim();
        if (p.EndsWith(@"\*", StringComparison.Ordinal))
            p = p[..^2];
        return NormalizeFolder(p);
    }

    private static string NormalizeFolder(string raw)
        => raw.Trim().TrimEnd('\\', '/');
}
