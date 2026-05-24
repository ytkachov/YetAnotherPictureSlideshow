using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace Yaps.Core.Models;

public class FinfoData
{
    public int? SchemaVersion { get; set; }
    public Rectangle[]? Faces { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? PlaceName { get; set; }
    public string? NominatimData { get; set; }
    public bool GeocodingAttempted { get; set; }
    public bool ExifReadFailed { get; set; }

    /// <summary>
    /// Corrected display orientation as an EXIF orientation code (1-8),
    /// written by the tools/orientation utility when a photo's pixels are
    /// rotated but its own metadata says otherwise. When set, it overrides
    /// whatever the file's EXIF Orientation tag claims. Null = not
    /// determined, in which case the EXIF tag (if any) is used.
    /// </summary>
    public int? Orientation { get; set; }

    /// <summary>
    /// Set by an orientation-detection pass (OrientationTagger / live
    /// screensaver) to mark that the model was already run on this photo,
    /// regardless of whether it produced an actionable rotation. Mirrors
    /// <see cref="GeocodingAttempted"/>: a subsequent pass sees the flag and
    /// skips re-detection. Lets us avoid re-running an 87 MB ONNX model
    /// against the same photo on every show.
    /// </summary>
    public bool OrientationDetectionAttempted { get; set; }

    // Bumped whenever the on-disk schema breaks the previous shape. Legacy
    // files written by Newtonsoft (no SchemaVersion field) deserialise as
    // null which is treated as "unknown / version 0". v2 added Orientation,
    // v3 added OrientationDetectionAttempted (both additive — older files
    // simply lack the fields and the detection pass picks them up).
    private const int CurrentSchemaVersion = 3;

    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Reads a .finfo file, transparently handling both the modern
    /// FinfoData JSON object and the legacy Rectangle[] array. Returns
    /// null if the file is missing, unreadable, or unparseable.
    /// </summary>
    public static FinfoData? ReadFromFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to read .finfo file {Path}", path);
            return null;
        }

        return TryDeserialize(json);
    }

    /// <summary>
    /// Parses .finfo JSON content. Detects the legacy Rectangle[] shape
    /// (the file starts with '[') and wraps it in a fresh FinfoData so
    /// callers always receive the modern type. Returns null on malformed
    /// or empty input — every caller already handles null.
    /// </summary>
    public static FinfoData? TryDeserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var trimmed = json.AsSpan().TrimStart();
        try
        {
            // Legacy format starts with '[' — a bare Rectangle[] array.
            // Modern format starts with '{' — a FinfoData object.
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var rectangles = ParseFaces(doc.RootElement);
                return rectangles == null ? null : new FinfoData { Faces = rectangles };
            }

            return DeserializeFinfo(doc.RootElement);
        }
        catch (Exception ex)
        {
            // Catch-all so a malformed .finfo never crashes the slideshow.
            // System.Text.Json throws JsonException for malformed JSON, but
            // NotSupportedException for unmapped types (e.g. Rectangle's
            // computed Top/Left/Right/Bottom properties under some BCL
            // versions) — both must be swallowed.
            Log.Warning(ex, "Failed to deserialize .finfo content");
            return null;
        }
    }

    private static FinfoData DeserializeFinfo(JsonElement root)
    {
        var result = new FinfoData();

        foreach (var prop in root.EnumerateObject())
        {
            switch (prop.Name.ToLowerInvariant())
            {
                case "schemaversion":
                    if (prop.Value.ValueKind == JsonValueKind.Number)
                        result.SchemaVersion = prop.Value.GetInt32();
                    break;
                case "faces":
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                        result.Faces = ParseFaces(prop.Value);
                    break;
                case "latitude":
                    if (prop.Value.ValueKind == JsonValueKind.Number)
                        result.Latitude = prop.Value.GetDouble();
                    break;
                case "longitude":
                    if (prop.Value.ValueKind == JsonValueKind.Number)
                        result.Longitude = prop.Value.GetDouble();
                    break;
                case "placename":
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        result.PlaceName = prop.Value.GetString();
                    break;
                case "nominatimdata":
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        result.NominatimData = prop.Value.GetString();
                    break;
                case "geocodingattempted":
                    if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        result.GeocodingAttempted = prop.Value.GetBoolean();
                    break;
                case "exifreadfailed":
                    if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        result.ExifReadFailed = prop.Value.GetBoolean();
                    break;
                case "orientation":
                    if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var orient))
                        result.Orientation = orient;
                    break;
                case "orientationdetectionattempted":
                    if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        result.OrientationDetectionAttempted = prop.Value.GetBoolean();
                    break;
            }
        }

        return result;
    }

    private static Rectangle[]? ParseFaces(JsonElement array)
    {
        var result = new Rectangle[array.GetArrayLength()];
        int i = 0;
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                // Newtonsoft RectangleConverter form: "x, y, w, h".
                var parts = element.GetString()!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 4
                    || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
                    || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
                    || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var w)
                    || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h))
                {
                    Log.Warning("Skipping unparseable legacy face rectangle {Value}", element.GetString());
                    continue;
                }
                result[i++] = new Rectangle(x, y, w, h);
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                // Object form. Read X/Y/Width/Height directly — relying on
                // STJ to map Rectangle is fragile because Location/Size/Top/
                // Bottom etc. complicate the converter's view of the type.
                if (TryGetInt(element, "X", out var x) &&
                    TryGetInt(element, "Y", out var y) &&
                    TryGetInt(element, "Width", out var w) &&
                    TryGetInt(element, "Height", out var h))
                {
                    result[i++] = new Rectangle(x, y, w, h);
                }
                else
                {
                    Log.Warning("Skipping face rectangle without X/Y/Width/Height");
                }
            }
        }

        if (i != result.Length)
            Array.Resize(ref result, i);
        return result;
    }

    private static bool TryGetInt(JsonElement obj, string name, out int value)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)
                && prop.Value.ValueKind == JsonValueKind.Number
                && prop.Value.TryGetInt32(out value))
                return true;
        }
        value = 0;
        return false;
    }

    /// <summary>
    /// Serialises and writes the data to disk with a stamped
    /// SchemaVersion. Indented for human-friendly diffing, matching the
    /// previous Newtonsoft Formatting.Indented output.
    /// </summary>
    public static void WriteToFile(string path, FinfoData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        data.SchemaVersion ??= CurrentSchemaVersion;
        var json = JsonSerializer.Serialize(data, _options);
        File.WriteAllText(path, json);
    }
}
