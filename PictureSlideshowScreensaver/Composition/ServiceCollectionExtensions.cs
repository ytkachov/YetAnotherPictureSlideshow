using Microsoft.Extensions.DependencyInjection;
using PictureSlideshowScreensaver.Models;
using PictureSlideshowScreensaver.ViewModels;

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
            services.AddSingleton<Settings>();
            services.AddSingleton<ImagesProvider, LocalImages>();

            services.AddTransient<ScreensaverViewModel>();
            services.AddTransient<Screensaver>();

            return services;
        }
    }
}
