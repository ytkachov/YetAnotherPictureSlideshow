using System.Threading;
using System.Threading.Tasks;
using Yaps.Core.Models;

namespace Yaps.Core.Abstractions;

/// <summary>
/// Reverse-geocoding contract: turn a (lat, lon) pair into a short
/// human-readable place name. Implementations are expected to honour
/// upstream rate limits internally (Nominatim asks for ≤1 req/sec).
/// Returns null on unrecoverable failure rather than throwing — every
/// existing caller already tolerates null.
/// </summary>
public interface IGeocoder
{
    Task<GeocodingResult?> ReverseGeocodeAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}
