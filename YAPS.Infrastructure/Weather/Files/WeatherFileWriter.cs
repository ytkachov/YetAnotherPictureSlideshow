using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Yaps.Core.Models.Weather;

namespace Yaps.Infrastructure.Weather.Files;

/// <summary>
/// JSON snapshot writer. File name is fixed so a future reader (or
/// support diagnostics) always knows where to look. Replaces the
/// legacy three-blob delimiter format — there are no consumers of
/// that format left after Stage 5.
/// </summary>
public sealed class WeatherFileWriter : IWeatherFileWriter
{
    private const string FileName = "weather_snapshot.json";

    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task WriteAsync(string folder, WeatherSnapshot? current, WeatherForecast? forecast, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException("Folder is required", nameof(folder));

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, FileName);
        var payload = new WeatherSnapshotFile
        {
            WrittenAtUtc = DateTimeOffset.UtcNow,
            Current = current,
            Forecast = forecast
        };

        try
        {
            await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, payload, _options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not write weather snapshot to {Path}", path);
        }
    }

    private sealed class WeatherSnapshotFile
    {
        public DateTimeOffset WrittenAtUtc { get; set; }
        public WeatherSnapshot? Current { get; set; }
        public WeatherForecast? Forecast { get; set; }
    }
}
