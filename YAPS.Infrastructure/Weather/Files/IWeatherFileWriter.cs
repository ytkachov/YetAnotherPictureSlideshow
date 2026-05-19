using System.Threading;
using System.Threading.Tasks;
using Yaps.Core.Models.Weather;

namespace Yaps.Infrastructure.Weather.Files;

/// <summary>
/// Writes a snapshot bundle to disk so WeatherCollector can keep an
/// out-of-process record of the latest reading. The screensaver no
/// longer consumes this file in Stage 5 (it polls in-process), but the
/// collector still updates it for future diagnostic / sharing use.
/// </summary>
public interface IWeatherFileWriter
{
    Task WriteAsync(
        string folder,
        WeatherSnapshot? current,
        WeatherForecast? forecast,
        CancellationToken cancellationToken = default);
}
