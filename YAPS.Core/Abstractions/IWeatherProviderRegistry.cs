using System.Collections.Generic;
using Yaps.Core.Models.Weather;

namespace Yaps.Core.Abstractions;

/// <summary>
/// Lookup over the providers registered with the container. <see cref="Resolve"/>
/// returns the singleton instance associated with <paramref name="name"/>;
/// throws <see cref="System.Collections.Generic.KeyNotFoundException"/> if the
/// name doesn't match a registered descriptor (a misconfigured Registry value
/// should be obvious at startup, not silent at runtime).
/// </summary>
public interface IWeatherProviderRegistry
{
    IReadOnlyList<WeatherProviderDescriptor> Available { get; }
    IWeatherProvider Resolve(string name);
}
