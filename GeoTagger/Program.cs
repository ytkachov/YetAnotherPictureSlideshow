using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using ExifLibrary;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Yaps.Core.Abstractions;
using Yaps.Core.Models;
using Yaps.Infrastructure;
using Yaps.Infrastructure.Settings;

class Program
{
    static double GpsArrayToDouble(Array arr)
    {
        if (arr == null || arr.Length != 3)
            throw new ArgumentException("Expected array of 3 rational elements");

        dynamic d = arr.GetValue(0), m = arr.GetValue(1), s = arr.GetValue(2);
        return (double)d.Numerator / (double)d.Denominator +
               (double)m.Numerator / (double)m.Denominator / 60.0 +
               (double)s.Numerator / (double)s.Denominator / 3600.0;
    }

    static List<string> SafeGetImages(string folderPath)
    {
        var result = new List<string>();
        SafeGetImagesRecursive(folderPath, result);
        return result;
    }

    static void SafeGetImagesRecursive(string folderPath, List<string> result)
    {
        // Files and directories are enumerated separately so a bad file
        // does not prevent descent into subdirectories. Enumerator-based
        // iteration lets us skip individual bad entries inside a folder.
        var files = TryEnumerate(() => Directory.EnumerateFiles(folderPath), folderPath, "files");
        if (files != null)
        {
            using (var e = files.GetEnumerator())
            {
                while (true)
                {
                    string f;
                    try
                    {
                        if (!e.MoveNext()) break;
                        f = e.Current;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Skipping bad file entry in: {Path}", folderPath);
                        break;
                    }
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".jpg" || ext == ".jpeg")
                        result.Add(f);
                }
            }
        }

        var dirs = TryEnumerate(() => Directory.EnumerateDirectories(folderPath), folderPath, "directories");
        if (dirs != null)
        {
            using (var e = dirs.GetEnumerator())
            {
                while (true)
                {
                    string d;
                    try
                    {
                        if (!e.MoveNext()) break;
                        d = e.Current;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Skipping bad directory entry in: {Path}", folderPath);
                        break;
                    }
                    SafeGetImagesRecursive(d, result);
                }
            }
        }
    }

    static IEnumerable<string> TryEnumerate(Func<IEnumerable<string>> get, string folderPath, string kind)
    {
        try { return get(); }
        catch (Exception ex)
        {
            Log.Warning(ex, "Cannot enumerate {Kind} in: {Path}", kind, folderPath);
            return null;
        }
    }

    static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .CreateLogger();

        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: GeoTagger <path-to-photos-folder>");
            return 1;
        }

        string folderPath = args[0];

        if (!Directory.Exists(folderPath))
        {
            Log.Error("Folder not found: {Folder}", folderPath);
            return 1;
        }

        // GeoTagger uses the same Infrastructure registrations the
        // screensaver does so the Nominatim rate-limit / User-Agent /
        // HttpClient pooling all match.
        var builder = Host.CreateApplicationBuilder();
        // Share the screensaver's photo-folder ↔ finfo-folder pairing so a
        // read-only library tagged here writes its .finfo to the same
        // configured folder the slideshow reads from.
        builder.Services.AddInfrastructure(RegistryConfig.ReadFinfoStoreOptions());
        using var host = builder.Build();
        await host.StartAsync();

        var geocoder = host.Services.GetRequiredService<IGeocoder>();
        var finfoStore = host.Services.GetRequiredService<IFinfoStore>();

        try
        {
            return await RunAsync(folderPath, geocoder, finfoStore);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    static async Task<int> RunAsync(string folderPath, IGeocoder geocoder, IFinfoStore finfoStore)
    {
        var imageFiles = SafeGetImages(folderPath).ToArray();

        Log.Information("Found {Count} JPEG files in {Folder} (including subfolders)", imageFiles.Length, folderPath);

        var geoCache = new Dictionary<string, GeocodingResult>();
        var geoNotFound = new HashSet<string>();

        int total = imageFiles.Length;
        int processed = 0;
        int taggedCount = 0;
        int skippedNoGps = 0;
        int skippedHasPlace = 0;
        int skippedAttempted = 0;
        int skippedBadExif = 0;
        int badExif = 0;
        int cacheHits = 0;
        int notFound = 0;
        int errors = 0;

        foreach (var imagePath in imageFiles)
        {
            processed++;

            if (processed % 100 == 0)
                Log.Information("Progress: {Processed}/{Total} | tagged: {Tagged} | cache: {Cache} | not found: {NotFound} | bad EXIF: {BadExif} | no GPS: {NoGps} | has place: {HasPlace} | attempted: {Attempted} | skipped bad: {SkipBad} | errors: {Errors}",
                    processed, total, taggedCount, cacheHits, notFound, badExif, skippedNoGps, skippedHasPlace, skippedAttempted, skippedBadExif, errors);

            // Read existing finfo first so we can short-circuit on stable terminal states
            // (has place, geocoding already attempted, EXIF previously unreadable).
            FinfoData existingData = finfoStore.Read(imagePath);

            if (existingData != null && !string.IsNullOrEmpty(existingData.PlaceName))
            {
                skippedHasPlace++;
                continue;
            }
            if (existingData != null && existingData.GeocodingAttempted)
            {
                skippedAttempted++;
                continue;
            }
            if (existingData != null && existingData.ExifReadFailed)
            {
                skippedBadExif++;
                continue;
            }

            // Read GPS from EXIF. ExifLibrary failures here mark the file so we don't retry next run.
            double? lat = null;
            double? lon = null;

            try
            {
                var reader = ImageFile.FromFile(imagePath);

                var latProp = reader.Properties[ExifTag.GPSLatitude];
                var latRefProp = reader.Properties[ExifTag.GPSLatitudeRef];
                var lonProp = reader.Properties[ExifTag.GPSLongitude];
                var lonRefProp = reader.Properties[ExifTag.GPSLongitudeRef];

                if (latProp?.Value is Array latArr && latArr.Length == 3 &&
                    lonProp?.Value is Array lonArr && lonArr.Length == 3)
                {
                    lat = GpsArrayToDouble(latArr);
                    lon = GpsArrayToDouble(lonArr);

                    var latRef = latRefProp?.Value?.ToString();
                    var lonRef = lonRefProp?.Value?.ToString();
                    if (latRef == "S" || latRef == "South") lat = -lat;
                    if (lonRef == "W" || lonRef == "West") lon = -lon;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "EXIF read failed for {File} — marking as ExifReadFailed", imagePath);
                try
                {
                    var data = existingData ?? new FinfoData();
                    data.ExifReadFailed = true;
                    finfoStore.Write(imagePath, data);
                }
                catch (Exception writeEx)
                {
                    Log.Error(writeEx, "Failed to write ExifReadFailed marker for {File}", imagePath);
                    errors++;
                }
                badExif++;
                continue;
            }

            try
            {
                if (lat == null || lon == null || double.IsNaN(lat.Value) || double.IsNaN(lon.Value) || double.IsInfinity(lat.Value) || double.IsInfinity(lon.Value))
                {
                    skippedNoGps++;
                    continue;
                }

                // Round to ~11m to group identical locations (4 decimals of degree)
                string coordKey = $"{lat.Value:F4},{lon.Value:F4}";

                GeocodingResult result = null;
                bool fromCache = false;
                if (geoCache.TryGetValue(coordKey, out var cached))
                {
                    result = cached;
                    fromCache = true;
                    cacheHits++;
                }
                else if (geoNotFound.Contains(coordKey))
                {
                    result = null;
                    fromCache = true;
                    cacheHits++;
                }
                else
                {
                    Log.Information("[{Processed}/{Total}] Geocoding {File} (lat={Lat}, lon={Lon})...",
                        processed, total, Path.GetFileName(imagePath), lat.Value, lon.Value);

                    result = await geocoder.ReverseGeocodeAsync(lat.Value, lon.Value);
                    if (result != null && !string.IsNullOrEmpty(result.PlaceName))
                        geoCache[coordKey] = result;
                    else
                        geoNotFound.Add(coordKey);
                }

                var data = existingData ?? new FinfoData
                {
                    Latitude = lat,
                    Longitude = lon
                };
                data.GeocodingAttempted = true;

                if (result != null && !string.IsNullOrEmpty(result.PlaceName))
                {
                    data.PlaceName = result.PlaceName;
                    data.NominatimData = result.FullResponse;

                    finfoStore.Write(imagePath, data);

                    if (!fromCache)
                        Log.Information("  -> {PlaceName}", result.PlaceName);
                    taggedCount++;
                }
                else
                {
                    finfoStore.Write(imagePath, data);

                    if (!fromCache)
                        Log.Warning("  -> No place name resolved (marked attempted)");
                    notFound++;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing {File}", imagePath);
                errors++;
            }
        }

        Log.Information("Done. Tagged: {Tagged}, Cache hits: {Cache}, Not found: {NotFound}, Bad EXIF: {BadExif}, Skipped (no GPS): {NoGps}, Skipped (has place): {HasPlace}, Skipped (attempted): {Attempted}, Skipped (bad EXIF): {SkipBad}, Errors: {Errors}",
            taggedCount, cacheHits, notFound, badExif, skippedNoGps, skippedHasPlace, skippedAttempted, skippedBadExif, errors);

        return errors > 0 ? 1 : 0;
    }
}
