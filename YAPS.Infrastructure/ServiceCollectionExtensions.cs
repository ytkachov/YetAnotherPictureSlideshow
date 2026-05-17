using System;
using Microsoft.Extensions.DependencyInjection;
using Yaps.Core.Abstractions;
using Yaps.Infrastructure.Geocoding;

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
}
