using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using OpenQA.Selenium;
using Serilog;
using Yaps.Core.Abstractions;
using Yaps.Infrastructure.Weather.Selenium;

namespace Yaps.Infrastructure.Weather.Providers;

/// <summary>
/// Scrapes the NSU weather page for the current air temperature in
/// Akademgorodok. Stage 5 promotes this from a hidden field inside the
/// Yandex provider to a first-class <see cref="ICurrentTemperatureOverride"/>
/// so the WeatherPollingService can apply it on top of any provider —
/// not just YandexApi.
/// </summary>
public sealed class NsuTemperatureOverride : ICurrentTemperatureOverride
{
    private const string Url = "http://weather.nsu.ru/old";

    private readonly SeleniumDriverFactory _factory;

    public NsuTemperatureOverride(SeleniumDriverFactory factory)
    {
        _factory = factory;
    }

    public string SourceName => "nsu";

    public Task<double?> GetCurrentTemperatureCelsiusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run<double?>(() =>
        {
            IWebDriver? driver = null;
            try
            {
                driver = _factory.Create();
                driver.Navigate().GoToUrl(Url);

                IWebElement? temp;
                try { temp = driver.FindElement(By.Id("temp")); }
                catch (NoSuchElementException) { return null; }

                var text = temp.Text;
                if (string.IsNullOrEmpty(text) || !text.Contains('°'))
                    return null;

                // weather.nsu.ru can render the decimal separator as "," or
                // "." depending on the page locale; normalise to "." so the
                // InvariantCulture parse works either way.
                var num = text.Substring(0, text.IndexOf('°')).Trim().Replace(',', '.');
                return double.TryParse(num, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : (double?)null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "NSU temperature scrape failed");
                return null;
            }
            finally
            {
                if (driver is not null)
                {
                    try { driver.Quit(); } catch (Exception ex) { Log.Warning(ex, "NSU driver Quit threw"); }
                    try { driver.Dispose(); } catch (Exception ex) { Log.Warning(ex, "NSU driver Dispose threw"); }
                }
            }
        }, cancellationToken);
    }
}
