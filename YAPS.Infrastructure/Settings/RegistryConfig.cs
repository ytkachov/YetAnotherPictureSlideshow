using Microsoft.Win32;
using Yaps.Core.Models;

namespace Yaps.Infrastructure.Settings;

/// <summary>
/// Reads the screensaver's registry configuration that the Infrastructure layer
/// needs. Kept here (a single Windows-only spot) so utilities — GeoTagger — can
/// reuse it without duplicating registry parsing: they get the same
/// photo-folder ↔ finfo-folder pairing the screensaver uses.
/// </summary>
public static class RegistryConfig
{
    private const string RegistryPath = @"SOFTWARE\PictureSlideshowScreensaver";

    /// <summary>
    /// Builds <see cref="FinfoStoreOptions"/> from the <c>ImageFolder</c> and
    /// <c>FinfoFolder</c> registry values. Returns <see cref="FinfoStoreOptions.Empty"/>
    /// (pure next-to-photo behaviour) when the key or values are absent.
    /// </summary>
    public static FinfoStoreOptions ReadFinfoStoreOptions()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryPath);
        if (key is null)
            return FinfoStoreOptions.Empty;

        var imageFolder = key.GetValue("ImageFolder") as string;
        var finfoFolder = key.GetValue("FinfoFolder") as string;
        return FinfoStoreOptions.FromConfig(imageFolder, finfoFolder);
    }
}
