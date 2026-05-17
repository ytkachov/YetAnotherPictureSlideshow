namespace Yaps.Core.Models;

/// <summary>
/// Result of a reverse-geocoding lookup. PlaceName is a short, display
/// friendly form ("Landmark, City" or "City") and may be null if the
/// provider didn't return enough detail. FullResponse is the raw
/// provider payload, kept so it can be cached / debugged offline.
/// </summary>
public sealed class GeocodingResult
{
    public string? PlaceName { get; init; }
    public string? FullResponse { get; init; }
}
