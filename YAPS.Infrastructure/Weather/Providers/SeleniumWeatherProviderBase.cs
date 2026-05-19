using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using OpenQA.Selenium;
using Serilog;
using Yaps.Core.Abstractions;
using Yaps.Core.Models.Weather;
using Yaps.Infrastructure.Weather.Selenium;

namespace Yaps.Infrastructure.Weather.Providers;

/// <summary>
/// Common scaffolding for providers that scrape a web page. Manages the
/// driver lifetime per call (the key correctness fix versus the legacy
/// long-lived <c>_driver</c> field), centralises the "navigate -> extract"
/// flow, and offers a couple of WebDriver convenience extensions the
/// concrete providers need.
/// </summary>
public abstract class SeleniumWeatherProviderBase : IWeatherProvider
{
    protected readonly SeleniumDriverFactory DriverFactory;

    protected SeleniumWeatherProviderBase(SeleniumDriverFactory driverFactory)
    {
        DriverFactory = driverFactory;
    }

    public abstract string Name { get; }
    public abstract WeatherCapabilities Capabilities { get; }

    public async Task<WeatherSnapshot?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (!Capabilities.HasFlag(WeatherCapabilities.Current))
            return null;

        return await RunDriverAsync(driver => ExtractCurrent(driver, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public async Task<WeatherForecast?> GetForecastAsync(CancellationToken cancellationToken = default)
    {
        if (!Capabilities.HasFlag(WeatherCapabilities.Forecast))
            return null;

        return await RunDriverAsync(driver => ExtractForecast(driver, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    protected abstract WeatherSnapshot? ExtractCurrent(IWebDriver driver, CancellationToken cancellationToken);
    protected abstract WeatherForecast? ExtractForecast(IWebDriver driver, CancellationToken cancellationToken);

    /// <summary>
    /// Selenium is synchronous; offload to a background task so the
    /// hosted polling service doesn't block whichever thread the host
    /// gives it. Driver is always disposed, even if the extractor throws.
    /// </summary>
    private async Task<T?> RunDriverAsync<T>(Func<IWebDriver, T?> body, CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            IWebDriver? driver = null;
            try
            {
                driver = DriverFactory.Create();
                return body(driver);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "{Provider} scrape failed", Name);
                return null;
            }
            finally
            {
                if (driver is not null)
                {
                    try { driver.Quit(); }
                    catch (Exception ex) { Log.Warning(ex, "WebDriver.Quit threw on {Provider}", Name); }
                    try { driver.Dispose(); }
                    catch (Exception ex) { Log.Warning(ex, "WebDriver.Dispose threw on {Provider}", Name); }
                }
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    protected static IWebElement? TryFind(ISearchContext context, By by)
    {
        try { return context.FindElement(by); }
        catch (NoSuchElementException) { return null; }
        catch (Exception ex) { Log.Debug(ex, "FindElement({By}) threw", by); return null; }
    }

    protected static ReadOnlyCollection<IWebElement> TryFindAll(ISearchContext context, By by)
    {
        try { return context.FindElements(by); }
        catch (Exception ex) { Log.Debug(ex, "FindElements({By}) threw", by); return new ReadOnlyCollection<IWebElement>(Array.Empty<IWebElement>()); }
    }

    protected static string OuterHtml(IWebDriver driver, IWebElement element)
    {
        return (string)((IJavaScriptExecutor)driver).ExecuteScript("return arguments[0].outerHTML;", element);
    }

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
