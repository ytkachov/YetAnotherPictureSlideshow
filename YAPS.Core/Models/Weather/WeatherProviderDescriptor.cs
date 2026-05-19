namespace Yaps.Core.Models.Weather;

/// <summary>
/// What the Configuration UI sees when listing providers: a stable id
/// (<see cref="Name"/>, the value persisted in Registry), a localised
/// label, and the feed capabilities. Stage 6 will surface this through
/// a ComboBox; Stage 5 only consumes it through the registry.
/// </summary>
public sealed record WeatherProviderDescriptor(
    string Name,
    string DisplayName,
    WeatherCapabilities Capabilities);
