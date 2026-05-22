using System;
using System.IO;
using informers;
using Microsoft.Extensions.DependencyInjection;
using PictureSlideshowScreensaver.Models;
using PictureSlideshowScreensaver.ViewModels;
using Yaps.Core.Abstractions;
using Yaps.Infrastructure;
using Yaps.Infrastructure.Faces;
using Yaps.Infrastructure.Settings;
using Yaps.Infrastructure.Weather;

namespace PictureSlideshowScreensaver.Composition
{
    internal static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the services the screensaver entry point pulls out of
        /// the container. ViewModels and the window are transient because
        /// each /s launch could spawn its own; Settings and the image
        /// library are long-lived singletons.
        /// </summary>
        public static IServiceCollection AddScreensaver(this IServiceCollection services)
        {
            // IFinfoStore is registered by AddInfrastructure(); the finfo-folder
            // pairing comes from the registry so the screensaver and the
            // utilities resolve sidecars to the same place.
            services.AddInfrastructure(RegistryConfig.ReadFinfoStoreOptions());

            // IClock and Settings are screensaver-level concerns.
            services.AddSingleton<IClock, SystemClock>();
            services.AddSingleton<Settings>();
            services.AddSingleton<ImagesProvider, LocalImages>();

            // Haar cascade XML is copied to output by the <Content> entry
            // in PictureSlideshowScreensaver.csproj. AppContext.BaseDirectory
            // is preferred over Assembly.GetEntryAssembly().Location because
            // the latter is empty under single-file publish.
            services.AddSingleton<IFaceDetector>(_ =>
            {
                var xmlPath = Path.Combine(AppContext.BaseDirectory, "recognition", "haarcascade_frontalface_alt2.xml");
                return new OpenCvFaceDetector(xmlPath);
            });

            // Weather subsystem. Options are populated from Settings using
            // the post-configure overload so both reads happen against a
            // single resolved Settings singleton.
            services.AddWeatherProviders();
            services.AddOptions<WeatherOptions>().Configure<Settings>((opts, settings) =>
            {
                opts.SelectedProvider = string.IsNullOrWhiteSpace(settings.WeatherProvider) ? "yandex-api" : settings.WeatherProvider;
                opts.YandexApiKey = settings.YandexApiKey;
                opts.PollingInterval = TimeSpan.FromMinutes(10);
            });
            services.AddHostedService<WeatherPollingService>();
            services.AddTransient<WeatherInformer>();

            services.AddTransient<ScreensaverViewModel>();
            services.AddTransient<Screensaver>();
            services.AddTransient<ConfigurationViewModel>();
            services.AddTransient<Configuration>();

            return services;
        }
    }
}
