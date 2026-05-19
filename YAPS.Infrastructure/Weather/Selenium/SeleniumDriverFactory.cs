using System;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;

namespace Yaps.Infrastructure.Weather.Selenium;

public enum SeleniumBrowser
{
    Chrome,
    Edge
}

/// <summary>
/// Per-request <see cref="IWebDriver"/> producer. The legacy code held
/// a driver in a field and reused it across calls, which is why driver
/// crashes leaked the process and required <c>Process.Kill("chrome")</c>
/// as a cleanup. Stage 5 inverts that: the provider creates a driver,
/// uses it once inside <c>using</c>, and lets the process die naturally.
/// </summary>
public sealed class SeleniumDriverFactory
{
    private readonly SeleniumBrowser _defaultBrowser;
    private readonly TimeSpan _pageLoadTimeout;

    public SeleniumDriverFactory(SeleniumBrowser defaultBrowser = SeleniumBrowser.Chrome, TimeSpan? pageLoadTimeout = null)
    {
        _defaultBrowser = defaultBrowser;
        _pageLoadTimeout = pageLoadTimeout ?? TimeSpan.FromSeconds(20);
    }

    public IWebDriver Create() => Create(_defaultBrowser);

    public IWebDriver Create(SeleniumBrowser browser)
    {
        IWebDriver driver = browser switch
        {
            SeleniumBrowser.Edge => BuildEdge(),
            SeleniumBrowser.Chrome => BuildChrome(),
            _ => throw new NotSupportedException($"Browser {browser} not supported")
        };
        driver.Manage().Timeouts().PageLoad = _pageLoadTimeout;
        return driver;
    }

    private static ChromeDriver BuildChrome()
    {
        var opts = new ChromeOptions();
        // Headless because the screensaver and the WeatherCollector both
        // run unattended; the legacy code popped a visible window each tick.
        opts.AddArgument("--headless=new");
        opts.AddArgument("--disable-gpu");
        opts.AddArgument("--no-sandbox");
        opts.AddArgument("--window-size=1280,1024");
        return new ChromeDriver(opts);
    }

    private static EdgeDriver BuildEdge()
    {
        var opts = new EdgeOptions();
        opts.AddArgument("--headless=new");
        opts.AddArgument("--disable-gpu");
        opts.AddArgument("--no-sandbox");
        opts.AddArgument("--window-size=1280,1024");
        return new EdgeDriver(opts);
    }
}
