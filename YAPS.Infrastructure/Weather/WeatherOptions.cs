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
    public string SelectedProvider { get; set; } = "open-meteo";

    // Default 60 min so a 30-req/day Yandex free-tier key (~one request every
    // ~48 min) has headroom for occasional retries and the WeatherCollector's
    // one-shot fetch. Overridable per-deployment via the WeatherPollingMinutes
    // Registry value; raise it for cheaper providers, lower it for self-hosted
    // ones. Combined with the per-tick fetch coalescing inside
    // YandexApiWeatherProvider this lands at ~24 API hits/day in steady state.
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMinutes(60);
    public string? YandexApiKey { get; set; }
    public bool ApplyCurrentTemperatureOverride { get; set; } = true;

    // Akademgorodok coordinates — same defaults as the legacy
    // YandexApiReader. Override via Registry when deploying elsewhere.
    public double Latitude { get; set; } = 54.85194397;
    public double Longitude { get; set; } = 83.10189056;
}
