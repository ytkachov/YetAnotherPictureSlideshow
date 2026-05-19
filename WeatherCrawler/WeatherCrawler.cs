using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yaps.Core.Abstractions;
using Yaps.Infrastructure;

namespace WeatherCrawler
{
  /// <summary>
  /// Manual debug harness: fetch current + forecast from the named
  /// provider once and dump them to stdout. Useful when fiddling with
  /// new providers without firing up the whole screensaver.
  /// </summary>
  internal class WeatherCrawlerApp
  {
    static async Task Main(string[] args)
    {
      string providerName = args.Length > 0 ? args[0] : "yandex-scrape";

      var builder = Host.CreateApplicationBuilder(args);
      builder.Services.AddInfrastructure();
      builder.Services.AddWeatherProviders(opts =>
      {
        opts.SelectedProvider = providerName;
        opts.ApplyCurrentTemperatureOverride = false;
      });
      using var host = builder.Build();
      await host.StartAsync().ConfigureAwait(false);

      try
      {
        var registry = host.Services.GetRequiredService<IWeatherProviderRegistry>();
        await using var provider = registry.Resolve(providerName);

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var current = await provider.GetCurrentAsync(cts.Token).ConfigureAwait(false);
        Console.WriteLine("current: " + (current is null ? "<null>" : System.Text.Json.JsonSerializer.Serialize(current)));

        var forecast = await provider.GetForecastAsync(cts.Token).ConfigureAwait(false);
        Console.WriteLine("forecast periods: " + (forecast?.Periods.Count ?? 0));
      }
      finally
      {
        await host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
      }
    }
  }
}
