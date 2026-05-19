using System;

namespace Yaps.Infrastructure.Weather;

/// <summary>
/// Runtime knobs for the weather subsystem. Populated from
/// <c>Settings</c> at composition time; consumers read through
/// <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/>.
/// Stage 5 only honours the values set at startup — Stage 6 will wire
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>
/// when the Configuration UI gains a provider ComboBox.
/// </summary>
public sealed class WeatherOptions
{
    public string SelectedProvider { get; set; } = "yandex-api";
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMinutes(10);
    public string? YandexApiKey { get; set; }
    public bool ApplyCurrentTemperatureOverride { get; set; } = true;

    // Akademgorodok coordinates — same defaults as the legacy
    // YandexApiReader. Override via Registry when deploying elsewhere.
    public double Latitude { get; set; } = 54.85194397;
    public double Longitude { get; set; } = 83.10189056;
}
