using System;
using System.Drawing;
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

    // Bumped whenever the on-disk schema breaks the previous shape. Legacy
    // files written by Newtonsoft (no SchemaVersion field) deserialise as
    // null which is treated as "unknown / version 0".
    private const int CurrentSchemaVersion = 1;

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
            if (trimmed.Length > 0 && trimmed[0] == '[')
            {
                // Legacy format: a bare array of detected face rectangles
                // written before FinfoData existed. Lift it into the new
                // shape so callers don't need to know the old layout.
                var rectangles = JsonSerializer.Deserialize<Rectangle[]>(json, _options);
                return rectangles == null ? null : new FinfoData { Faces = rectangles };
            }

            return JsonSerializer.Deserialize<FinfoData>(json, _options);
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Failed to deserialize .finfo content");
            return null;
        }
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
