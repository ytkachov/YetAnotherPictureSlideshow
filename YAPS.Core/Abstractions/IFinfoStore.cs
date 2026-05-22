using System;
using System.IO;
using Yaps.Core.Models;

namespace Yaps.Core.Abstractions;

/// <summary>
/// Abstraction over .finfo persistence. Callers pass the <b>image</b> path;
/// the store resolves where that photo's .finfo lives — next to the photo by
/// default, or, when a paired finfo folder is configured, mirrored under that
/// folder (see <see cref="FinfoStoreOptions"/>). Introducing the seam lets
/// callers be substituted in tests (in-memory store) or in future (e.g. a
/// SQLite-backed store) without changing the consumers.
/// </summary>
public interface IFinfoStore
{
    FinfoData? Read(string imagePath);
    void Write(string imagePath, FinfoData data);
}

public sealed class FileFinfoStore : IFinfoStore
{
    private readonly FinfoStoreOptions _options;

    // Parameterless default keeps the legacy "next to the photo" behaviour for
    // callers that construct the store without DI (e.g. LocalImageInfo's fallback).
    public FileFinfoStore(FinfoStoreOptions? options = null)
        => _options = options ?? FinfoStoreOptions.Empty;

    public FinfoData? Read(string imagePath)
    {
        if (string.IsNullOrEmpty(imagePath))
            return null;

        string finfoPath = ResolveFinfoPath(imagePath);
        var data = FinfoData.ReadFromFile(finfoPath);
        if (data != null)
            return data;

        // Legacy fallback: a sidecar may already sit next to the photo from
        // before a finfo folder was configured. Read it until it's re-written
        // into the configured folder.
        string sidecar = SidecarPath(imagePath);
        if (!PathEquals(sidecar, finfoPath))
            return FinfoData.ReadFromFile(sidecar);

        return null;
    }

    public void Write(string imagePath, FinfoData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        string finfoPath = ResolveFinfoPath(imagePath);

        string? dir = Path.GetDirectoryName(finfoPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        FinfoData.WriteToFile(finfoPath, data);
    }

    /// <summary>
    /// Maps an image path to its .finfo path. Longest-prefix matches the image
    /// against the configured photo folders; if the matched pair names a
    /// (non-empty) finfo folder, the .finfo mirrors the image's subtree under
    /// it. Otherwise the sidecar sits next to the photo.
    /// </summary>
    private string ResolveFinfoPath(string imagePath)
    {
        string full = SafeFullPath(imagePath);

        FinfoFolderPair? best = null;
        int bestLen = -1;
        foreach (var pair in _options.Pairs)
        {
            if (pair.FinfoFolder.Length == 0)
                continue;

            string photoFull = SafeFullPath(pair.PhotoFolder);
            if (photoFull.Length > bestLen && IsUnder(full, photoFull))
            {
                best = pair;
                bestLen = photoFull.Length;
            }
        }

        if (best is null)
            return Path.ChangeExtension(full, "finfo");

        string baseFull = SafeFullPath(best.PhotoFolder);
        string relative = Path.GetRelativePath(baseFull, full);
        string mirrored = Path.Combine(SafeFullPath(best.FinfoFolder), relative);
        return Path.ChangeExtension(mirrored, "finfo");
    }

    private static string SidecarPath(string imagePath)
        => Path.ChangeExtension(SafeFullPath(imagePath), "finfo");

    private static bool IsUnder(string fullPath, string folderFull)
    {
        if (fullPath.Length <= folderFull.Length)
            return string.Equals(fullPath, folderFull, StringComparison.OrdinalIgnoreCase);

        return fullPath.StartsWith(folderFull, StringComparison.OrdinalIgnoreCase)
               && (fullPath[folderFull.Length] == Path.DirectorySeparatorChar
                   || fullPath[folderFull.Length] == Path.AltDirectorySeparatorChar);
    }

    private static bool PathEquals(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string SafeFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }
}
