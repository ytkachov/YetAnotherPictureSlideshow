using System;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Yaps.Core.Abstractions;

namespace Yaps.Infrastructure.Weather.Providers;

/// <summary>
/// Reads the current air temperature in Akademgorodok from weather.nsu.ru.
/// The visible page (<c>/old/</c>) leaves <c>&lt;span id="temp"&gt;</c> empty
/// and fills it from JavaScript, which is why the legacy implementation drove
/// a headless Chrome via Selenium. That browser spin-up was the source of the
/// <c>chromedriver.exe</c> startup popup on the frame and a per-tick process
/// leak. This version skips the browser entirely and hits the same data
/// endpoint the page's JS calls — <c>loadata.php</c> returns a small script
/// blob in which the current temperature appears verbatim as
/// <c>id = 'temp'; ... innerHTML = '24.6&amp;deg;C'</c>.
/// </summary>
public sealed class NsuTemperatureOverride : ICurrentTemperatureOverride
{
    // Relative to the HttpClient BaseAddress (http://weather.nsu.ru/). The
    // page's JS calls "../loadata.php" from /old/, which resolves to the root.
    private const string DataPath = "loadata.php?tick={0}&rand={1}&std=three";

    // The temperature block in the loadata.php response looks like:
    //   id = 'temp'; var cnv = ...getElementById(id)...; if(cnv) cnv.innerHTML = '24.6&deg;C';
    // Anchor on the 'temp' id (the leading quote keeps it from matching
    // 'avertemp', which carries the daily average), then grab the first
    // degree-terminated value that follows.
    private static readonly Regex TempRegex = new(
        @"id\s*=\s*'temp'\s*;[\s\S]*?innerHTML\s*=\s*'([^']*?)(?:&deg;|°)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly HttpClient _http;

    public NsuTemperatureOverride(HttpClient http)
    {
        _http = http;
    }

    public string SourceName => "nsu";

    public async Task<double?> GetCurrentTemperatureCelsiusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var tick = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var rand = Random.Shared.NextDouble().ToString("R", CultureInfo.InvariantCulture);
            var url = string.Format(CultureInfo.InvariantCulture, DataPath, tick, rand);

            var body = await _http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

            var match = TempRegex.Match(body);
            if (!match.Success)
                return null;

            // weather.nsu.ru renders the decimal separator as "," or "."
            // depending on page locale, and uses U+2212 for the minus sign;
            // normalise both so the InvariantCulture parse works either way.
            var num = match.Groups[1].Value.Replace(',', '.').Replace('−', '-').Trim();
            return double.TryParse(num, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : (double?)null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "NSU temperature fetch failed");
            return null;
        }
    }
}
