using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using Serilog;
using Serilog.Events;
using Yaps.Core.Abstractions;
using Yaps.Core.Models.Weather;
using Yaps.Infrastructure;
using Yaps.Infrastructure.Weather;
using Yaps.Infrastructure.Weather.Files;

namespace WeatherCollector
{
  class WeatherCollectorApp
  {
    static async Task<int> Main(string[] args)
    {
      InitLogger();
      Log.Information("Start weather collector args={ArgCount}", args.Length);

      try
      {
        var folder = args.Length > 0 ? args[0] : AppContext.BaseDirectory;
        var providerName = args.Length > 1 ? args[1] : "yandex-api";
        bool nocheck = args.Length > 2;

        if (!nocheck && !IsScreensaverRunning())
        {
          Log.Information("WeatherCollector exit: screensaver process not running");
          return 0;
        }

        TaskSchedulerInstaller.Ensure($"\"{folder}\" {providerName}");

        var apiKey = ReadRegistryString("YandexApiKey");
        using var host = Host.CreateApplicationBuilder(args)
            .ConfigureWeatherServices(providerName, apiKey)
            .Build();
        await host.StartAsync().ConfigureAwait(false);

        try
        {
          var registry = host.Services.GetRequiredService<IWeatherProviderRegistry>();
          await using var provider = registry.Resolve(providerName);

          using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

          WeatherSnapshot current = null;
          WeatherForecast forecast = null;

          if (provider.Capabilities.HasFlag(WeatherCapabilities.Current))
          {
            try { current = await provider.GetCurrentAsync(cts.Token).ConfigureAwait(false); }
            catch (Exception ex) { Log.Warning(ex, "GetCurrentAsync failed"); }
          }
          if (provider.Capabilities.HasFlag(WeatherCapabilities.Forecast))
          {
            try { forecast = await provider.GetForecastAsync(cts.Token).ConfigureAwait(false); }
            catch (Exception ex) { Log.Warning(ex, "GetForecastAsync failed"); }
          }

          var writer = host.Services.GetRequiredService<IWeatherFileWriter>();
          await writer.WriteAsync(folder, current, forecast, cts.Token).ConfigureAwait(false);
        }
        finally
        {
          await host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }

        return 0;
      }
      catch (Exception ex)
      {
        Log.Error(ex, "WeatherCollector terminated unexpectedly");
        return 1;
      }
      finally
      {
        Log.Information("Finish weather collector");
        Log.CloseAndFlush();
      }
    }

    private static bool IsScreensaverRunning()
    {
      foreach (var p in Process.GetProcesses())
      {
        if (string.Equals(p.ProcessName, "PictureSlideshowScreensaver", StringComparison.OrdinalIgnoreCase))
          return true;
      }
      return false;
    }

    private static string ReadRegistryString(string name)
    {
      using var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\PictureSlideshowScreensaver");
      return key?.GetValue(name) as string;
    }

    private static void InitLogger()
    {
      using var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\PictureSlideshowScreensaver");
      if (key == null) return;

      bool writeLog = int.TryParse((string)key.GetValue("WriteLog") ?? "0", out var w) && w == 1;
      string writeLogPath = (string)key.GetValue("WriteLogFolder");
      if (!writeLog || string.IsNullOrEmpty(writeLogPath)) return;
      // Creating the log folder runs before the logger itself exists, so a
      // failure here can't be Log'd. The Directory.Exists guard below means
      // a failed mkdir downgrades to "no log writer" rather than crashing.
      try { Directory.CreateDirectory(writeLogPath); } catch { return; }
      if (!Directory.Exists(writeLogPath)) return;

      const string template = "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message}{NewLine}{Exception}";
      Log.Logger = new LoggerConfiguration()
          .MinimumLevel.Verbose()
          .WriteTo.Async(a => a.File(Path.Combine(writeLogPath, "wc_verbose_log-.txt"), outputTemplate: template, flushToDiskInterval: TimeSpan.FromSeconds(10), rollingInterval: RollingInterval.Day))
          .WriteTo.Async(a => a.File(Path.Combine(writeLogPath, "wc_information_log-.txt"), outputTemplate: template, restrictedToMinimumLevel: LogEventLevel.Information, flushToDiskInterval: TimeSpan.FromSeconds(10), rollingInterval: RollingInterval.Day))
          .WriteTo.Async(a => a.File(Path.Combine(writeLogPath, "wc_warning_log-.txt"), outputTemplate: template, restrictedToMinimumLevel: LogEventLevel.Warning, flushToDiskInterval: TimeSpan.FromSeconds(10), rollingInterval: RollingInterval.Day))
          .WriteTo.Async(a => a.File(Path.Combine(writeLogPath, "wc_error_log-.txt"), outputTemplate: template, restrictedToMinimumLevel: LogEventLevel.Error, flushToDiskInterval: TimeSpan.FromSeconds(1), rollingInterval: RollingInterval.Day))
          .CreateLogger();
    }
  }

  internal static class HostBuilderExtensions
  {
    public static HostApplicationBuilder ConfigureWeatherServices(this HostApplicationBuilder builder, string providerName, string apiKey)
    {
      builder.Services.AddInfrastructure();
      builder.Services.AddWeatherProviders(opts =>
      {
        opts.SelectedProvider = providerName;
        opts.YandexApiKey = apiKey;
        // Collector is one-shot — polling interval irrelevant; override
        // disabled to keep the run short (NSU scrape can take ~5s).
        opts.ApplyCurrentTemperatureOverride = false;
      });
      return builder;
    }
  }
}
