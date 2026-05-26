using System;
using System.IO;
using informers;
using Microsoft.Extensions.DependencyInjection;
using PictureSlideshowScreensaver.Models;
using PictureSlideshowScreensaver.ViewModels;
using Yaps.Core.Abstractions;
using Yaps.Infrastructure;
using Yaps.Infrastructure.Faces;
using Yaps.Infrastructure.Images;
using Yaps.Infrastructure.Orientation;
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

            // ONNX orientation detector. Same recognition\ folder + linked from
            // tools/orientation/orientation.onnx by the csproj so utility and
            // screensaver share a single copy of the 88 MB model file. The
            // session is reused for the process lifetime; Run is thread-safe.
            services.AddSingleton<IOrientationDetector>(_ =>
            {
                var onnxPath = Path.Combine(AppContext.BaseDirectory, "recognition", "orientation.onnx");
                return new OnnxOrientationDetector(onnxPath);
            });

            // Stage 4 split: the bitmap pipeline (decode + ONNX orientation +
            // Haar face detection + .finfo persist) lives in the loader so
            // LocalImageInfo can stay a thin wrapper over ImageMetadata.
            // Stateless across calls — see WpfImageBitmapLoader's class doc
            // for why concurrent access is safe by design.
            //
            // Stage 6.7b: the primary screen's pixel width is used as the
            // DecodePixelWidth hint so 24 MP photos shown on a 1080p frame
            // retain a ~2 MB pixel buffer instead of ~50 MB. Forms.Screen
            // (rather than SystemParameters) so the value matches what
            // App.xaml.cs uses to size the screensaver window — same DPI
            // model, no surprises on multi-monitor.
            services.AddSingleton<IImageBitmapLoader>(sp =>
            {
                var faceDetector = sp.GetRequiredService<IFaceDetector>();
                var orientationDetector = sp.GetService<IOrientationDetector>();
                var finfoStore = sp.GetRequiredService<IFinfoStore>();
                int screenWidth = System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Width ?? 0;
                return new WpfImageBitmapLoader(faceDetector, orientationDetector, finfoStore, screenWidth);
            });

            // Weather subsystem. Options are populated from Settings using
            // the post-configure overload so both reads happen against a
            // single resolved Settings singleton.
            services.AddWeatherProviders();
            services.AddOptions<WeatherOptions>().Configure<Settings>((opts, settings) =>
            {
                opts.SelectedProvider = string.IsNullOrWhiteSpace(settings.WeatherProvider) ? "open-meteo" : settings.WeatherProvider;
                opts.YandexApiKey = settings.YandexApiKey;
                opts.PollingInterval = TimeSpan.FromMinutes(settings.WeatherPollingMinutes);
            });
            services.AddHostedService<WeatherPollingService>();

            // Stage 6.2b: weather widgets bind to the per-period informers
            // exposed by ForecastViewModel (pushed via DataContext) instead
            // of resolving a WeatherInformer through the service locator.
            services.AddTransient<ForecastViewModel>();

            services.AddTransient<ScreensaverViewModel>();
            services.AddTransient<Screensaver>();
            services.AddTransient<ConfigurationViewModel>();
            services.AddTransient<Configuration>();
            services.AddTransient<LogViewerViewModel>();
            services.AddTransient<LogViewer>();

            return services;
        }
    }
}
