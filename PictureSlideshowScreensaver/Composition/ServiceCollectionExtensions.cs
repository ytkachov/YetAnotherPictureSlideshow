using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using PictureSlideshowScreensaver.Models;
using PictureSlideshowScreensaver.ViewModels;
using Yaps.Core.Abstractions;
using Yaps.Infrastructure;
using Yaps.Infrastructure.Faces;

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
            services.AddInfrastructure();

            // IFinfoStore is registered by AddInfrastructure(); IClock and
            // Settings are screensaver-level concerns.
            services.AddSingleton<IClock, SystemClock>();
            services.AddSingleton<Settings>();
            services.AddSingleton<ImagesProvider, LocalImages>();

            // Haar cascade XML ships next to the .exe via the Resource
            // entry in PictureSlideshowScreensaver.csproj. AppContext is
            // preferred over Assembly.GetEntryAssembly().Location because
            // the latter is empty under single-file publish.
            services.AddSingleton<IFaceDetector>(_ =>
            {
                var xmlPath = Path.Combine(AppContext.BaseDirectory, "recognition", "haarcascade_frontalface_alt2.xml");
                if (!File.Exists(xmlPath))
                    xmlPath = Path.Combine(AppContext.BaseDirectory, "haarcascade_frontalface_alt2.xml");
                return new OpenCvFaceDetector(xmlPath);
            });

            services.AddTransient<ScreensaverViewModel>();
            services.AddTransient<Screensaver>();

            return services;
        }
    }
}
