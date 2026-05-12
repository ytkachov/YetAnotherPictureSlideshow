using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ExifLibrary;
using Serilog;

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

        var imageFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(f =>
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                return ext == ".jpg" || ext == ".jpeg";
            })
            .ToArray();

        Log.Information("Found {Count} JPEG files in {Folder} (including subfolders)", imageFiles.Length, folderPath);

        int total = imageFiles.Length;
        int processed = 0;
        int taggedCount = 0;
        int skippedNoGps = 0;
        int skippedHasPlace = 0;
        int errors = 0;

        foreach (var imagePath in imageFiles)
        {
            processed++;
            string finfoPath = Path.ChangeExtension(imagePath, "finfo");

            if (processed % 100 == 0)
                Log.Information("Progress: {Processed}/{Total} | tagged: {Tagged} | no GPS: {NoGps} | has place: {HasPlace} | errors: {Errors}",
                    processed, total, taggedCount, skippedNoGps, skippedHasPlace, errors);

            try
            {
                // Read GPS from EXIF
                double? lat = null;
                double? lon = null;

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

                if (lat == null || lon == null)
                {
                    skippedNoGps++;
                    continue;
                }

                // Check existing finfo
                FinfoData existingData = null;
                if (File.Exists(finfoPath))
                {
                    try
                    {
                        var json = File.ReadAllText(finfoPath);
                        existingData = JsonConvert.DeserializeObject<FinfoData>(json);
                    }
                    catch { }
                }

                // Skip if already has place name
                if (existingData != null && !string.IsNullOrEmpty(existingData.PlaceName))
                {
                    skippedHasPlace++;
                    continue;
                }

                Log.Information("[{Processed}/{Total}] Geocoding {File} (lat={Lat}, lon={Lon})...",
                    processed, total, Path.GetFileName(imagePath), lat.Value, lon.Value);

                var result = await GeocodingService.ReverseGeocodeAsync(lat.Value, lon.Value);

                if (result != null && !string.IsNullOrEmpty(result.PlaceName))
                {
                    var data = existingData ?? new FinfoData
                    {
                        Latitude = lat,
                        Longitude = lon
                    };

                    data.PlaceName = result.PlaceName;
                    data.NominatimData = result.FullResponse;

                    string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                    File.WriteAllText(finfoPath, json);

                    Log.Information("  -> {PlaceName}", result.PlaceName);
                    taggedCount++;
                }
                else
                {
                    Log.Warning("  -> No place name resolved");
                    errors++;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing {File}", imagePath);
                errors++;
            }
        }

        Log.Information("Done. Tagged: {Tagged}, Skipped (no GPS): {NoGps}, Skipped (has place): {HasPlace}, Errors: {Errors}",
            taggedCount, skippedNoGps, skippedHasPlace, errors);

        return errors > 0 ? 1 : 0;
    }
}
