using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Yaps.Core.Abstractions;
using Yaps.Core.Models.Weather;

namespace Yaps.Infrastructure.Weather;

/// <summary>
/// Looks providers up by name out of the DI container. Resolution is
/// deferred (we never call <see cref="GetRequiredKeyedService"/> in the
/// ctor) so a missing impl shows up only when actually requested — which
/// matters when WeatherCollector wants one provider and the screensaver
/// wants another, with both sharing the same registration extension.
/// </summary>
public sealed class WeatherProviderRegistry : IWeatherProviderRegistry
{
    private readonly IServiceProvider _services;
    private readonly IReadOnlyList<WeatherProviderDescriptor> _descriptors;
    private readonly HashSet<string> _knownNames;

    public WeatherProviderRegistry(
        IServiceProvider services,
        IEnumerable<WeatherProviderDescriptor> descriptors)
    {
        _services = services;
        _descriptors = descriptors.ToList();
        _knownNames = new HashSet<string>(_descriptors.Select(d => d.Name), StringComparer.Ordinal);
    }

    public IReadOnlyList<WeatherProviderDescriptor> Available => _descriptors;

    public IWeatherProvider Resolve(string name)
    {
        if (!_knownNames.Contains(name))
            throw new KeyNotFoundException($"Weather provider '{name}' is not registered. Available: {string.Join(", ", _knownNames)}.");

        return _services.GetRequiredKeyedService<IWeatherProvider>(name);
    }
}
