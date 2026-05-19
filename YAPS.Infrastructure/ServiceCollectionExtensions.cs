using System;
using Microsoft.Extensions.DependencyInjection;
using Yaps.Core.Abstractions;
using Yaps.Core.Models.Weather;
using Yaps.Infrastructure.Geocoding;
using Yaps.Infrastructure.Weather;
using Yaps.Infrastructure.Weather.Files;
using Yaps.Infrastructure.Weather.Providers;
using Yaps.Infrastructure.Weather.Selenium;

namespace Yaps.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires up Infrastructure-layer services. HttpClient lifecycle is
    /// owned by HttpClientFactory so we get pooled handler reuse and
    /// DNS refresh without dragging the static HttpClient pattern into
    /// Infrastructure.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IFinfoStore, FileFinfoStore>();

        services.AddHttpClient<IGeocoder, NominatimGeocoder>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            // Nominatim's usage policy requires a real User-Agent.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("YetAnotherPictureSlideshow/1.0 (+https://github.com/ytkachov/YetAnotherPictureSlideshow)");
        });

        return services;
    }

    /// <summary>
    /// Registers every weather provider, the snapshot store, the
    /// temperature override, and the lookup registry. Hosted polling
    /// (<c>WeatherPollingService</c>) is left to the caller —
    /// WeatherCollector wants one-shot fetches, the screensaver wants
    /// continuous polling, both share these registrations.
    /// </summary>
    public static IServiceCollection AddWeatherProviders(
        this IServiceCollection services,
        Action<WeatherOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);
        else
            services.AddOptions<WeatherOptions>();

        services.AddSingleton<SeleniumDriverFactory>();
        services.AddSingleton<IWeatherFileWriter, WeatherFileWriter>();

        services.AddSingleton<WeatherSnapshotStore>();
        services.AddSingleton<IWeatherSnapshotStore>(sp => sp.GetRequiredService<WeatherSnapshotStore>());
        services.AddSingleton<IWritableWeatherSnapshotStore>(sp => sp.GetRequiredService<WeatherSnapshotStore>());

        services.AddHttpClient<YandexApiWeatherProvider>(client =>
        {
            client.BaseAddress = new Uri("https://api.weather.yandex.ru/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Keyed singletons so the registry can resolve by Registry-stored
        // provider name. Each provider self-reports its Name; keep them
        // in sync with the descriptor list below.
        services.AddKeyedSingleton<IWeatherProvider>("yandex-api",
            (sp, _) => sp.GetRequiredService<YandexApiWeatherProvider>());
        services.AddKeyedSingleton<IWeatherProvider, YandexScraperWeatherProvider>("yandex-scrape");
        services.AddKeyedSingleton<IWeatherProvider, NgsScraperWeatherProvider>("ngs-scrape");

        services.AddSingleton(new WeatherProviderDescriptor("yandex-api",   "Yandex Weather API", WeatherCapabilities.All));
        services.AddSingleton(new WeatherProviderDescriptor("yandex-scrape","Yandex Pogoda (scrape)", WeatherCapabilities.All));
        services.AddSingleton(new WeatherProviderDescriptor("ngs-scrape",   "NGS Pogoda (Akademgorodok)", WeatherCapabilities.All));

        services.AddSingleton<ICurrentTemperatureOverride, NsuTemperatureOverride>();
        services.AddSingleton<IWeatherProviderRegistry, WeatherProviderRegistry>();

        return services;
    }
}
