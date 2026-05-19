using System.Threading;
using System.Threading.Tasks;

namespace Yaps.Core.Abstractions;

/// <summary>
/// Point-thermometer reading that should replace a provider's reported
/// current temperature when available. Registered as a multi-binding —
/// the polling service applies every implementation in turn, so future
/// extra sensors can stack on top of the existing NSU one without
/// touching the providers themselves.
///
/// The default <c>NsuTemperatureOverride</c> scrapes weather.nsu.ru;
/// it's materially more accurate for "current temperature in
/// Akademgorodok" than any official station Yandex reports.
/// </summary>
public interface ICurrentTemperatureOverride
{
    string SourceName { get; }
    Task<double?> GetCurrentTemperatureCelsiusAsync(CancellationToken cancellationToken = default);
}
