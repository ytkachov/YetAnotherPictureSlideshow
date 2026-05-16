using System;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;

using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using Serilog;

namespace weather
{
  public enum WeatherSource
  {
    NW = 0, // NGS Watin (removed; kept in enum for backward-compat with stored values)
    NC = 1, // NGS Chrome
    NI = 2, // NGS IE (removed; kept in enum for backward-compat with stored values)
    NE = 3, // NGS Edge
    YC = 4, // Yandex Chrome
    YI = 5, // Yandex IE (removed; kept in enum for backward-compat with stored values)
    YE = 6, // Yandex Edge
    YAC = 100  // Yandex API Chrome
  }

  public interface IWeatherReader
  {
    void close();
    void restart();
    string temperature();
    string forecast();
    string current();
    void getrest();
  }

  public static class XmlExtensions
  {
    public static XmlNodeList SelectCellDivs(this XmlNode tr, string selector)
    {
      XmlNode tc = tr.SelectSingleNode(string.Format("./td[@class='{0}']", selector));
      if (tc == null)
        throw new Exception("cant find requested table cell");

      return tc.SelectNodes("./div");
    }
  }

  public static class SeleniumExtensions
  {
    public static string ReplaceAt(this string str, int index, int length, string replace)
    {
      return str.Remove(index, Math.Min(length, str.Length - index))
              .Insert(index, replace);
    }

    public static string correctOuterHTML(this IWebDriver driver, IWebElement element)
    {
      string outerhtml = driver.outerHTML(element);

      string img = "<img\\s[^>]*?src\\s*=\\s*['\\\"]([^ '\\\"]*?)['\\\"][^>]*?>";
      var matches = Regex.Matches(outerhtml, img);
      for (int i = matches.Count - 1; i >= 0; i--)
      {
        string instr = matches[i].Value;
        string outstr = instr.Substring(0, instr.Length - 1) + "/>";
        outerhtml = outerhtml.ReplaceAt(matches[i].Index, matches[i].Length, outstr);
      }

      outerhtml = outerhtml.Replace("<br>", " ").Replace("</br>", " ").Replace("&nbsp;", " ");
      return outerhtml;
    }

    public static IWebElement findElement(this ISearchContext self, By by)
    {
      if (self == null)
        return null;

      IWebElement el = null;
      try
      {
        el = self.FindElement(by);
      }
      catch (Exception ex)
      {
        Log.Error(ex, "");
      }

      return el;
    }

    public static ReadOnlyCollection<IWebElement> findElements(this ISearchContext self, By by)
    {
      if (self == null)
        return null;

      ReadOnlyCollection<IWebElement> els = null;
      try
      {
        els = self.FindElements(by);
      }
      catch (Exception ex)
      {
        Log.Error(ex, "");
      }

      return els;
    }

    public static string outerHTML(this IWebDriver self, IWebElement el)
    {
      if (self == null)
        return null;

      String contents = (String)((IJavaScriptExecutor)self).ExecuteScript("return arguments[0].outerHTML;", el);
      return contents;
    }

    public static string innerHTML(this IWebDriver self, IWebElement el)
    {
      if (self == null)
        return null;

      String contents = (String)((IJavaScriptExecutor)self).ExecuteScript("return arguments[0].innerHTML;", el);
      return contents;
    }
  }

  public abstract class WeatherSeleniumReader : IWeatherReader, IDisposable
  {
    protected IWebDriver _driver = null;
    protected WeatherSource _type;
    protected string _weather_url;
    protected string _weather_forecast_url;
    private bool _disposed;

    public WeatherSeleniumReader(WeatherSource type)
    {
      _type = type;
      // If create_driver throws (e.g. browser binary missing) we'd otherwise
      // leak whatever the driver process already started; catch and rethrow
      // so the partial state is visible.
      try
      {
        _driver = create_driver();
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Failed to create WebDriver for {Type}", type);
        throw;
      }
    }

    public void close()
    {
      var driver = _driver;
      _driver = null;
      if (driver == null)
        return;

      try
      {
        driver.Close();
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "WebDriver.Close threw");
      }

      try
      {
        driver.Quit();
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "WebDriver.Quit threw");
      }

      try
      {
        driver.Dispose();
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "WebDriver.Dispose threw");
      }
    }

    public void Dispose()
    {
      if (_disposed)
        return;
      _disposed = true;
      close();
      GC.SuppressFinalize(this);
    }

    public virtual string current()
    {
      navigate(_weather_url);
      return get_current();
    }

    public virtual string forecast()
    {
      if (_driver.Url != _weather_forecast_url)
        navigate(_weather_forecast_url);

      return get_forecast();
    }

    public virtual string temperature()
    {
      navigate();
      Thread.Sleep(1000);

      return get_temperature();
    }

    public void restart()
    {
      // Reuse close()'s defensive teardown rather than calling Close/Quit
      // directly: if either method throws (which selenium 4 does on a
      // crashed browser) we still want the driver disposed and _driver
      // nulled so the next navigate() recreates it.
      close();
    }

    public void getrest()
    {
      _driver.Navigate().GoToUrl("http://google.com/");
    }

    protected virtual IWebDriver create_driver()
    {
      IWebDriver driver;
      if (_type == WeatherSource.NE || _type == WeatherSource.YE)
        driver = new EdgeDriver();
      else if (_type == WeatherSource.NC || _type == WeatherSource.YC)
        driver = new ChromeDriver();
      else
        throw new NotSupportedException($"WeatherSource {_type} is no longer supported (IE/WatiN removed)");

      driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(20);

      return driver;
    }

    protected virtual void navigate(string url = null)
    {
      if (_driver == null)
        _driver = create_driver();

      try
      {
        if (url == null)
          _driver.Navigate().GoToUrl("http://weather.nsu.ru/old");
        else
          _driver.Navigate().GoToUrl(url);
      }
      catch (Exception ex)
      {
        Log.Error(ex, "");
      }
    }

    protected virtual string get_temperature()
    {
      var temp = _driver.findElement(By.Id("temp"));
      if (temp != null)
      {
        Thread.Sleep(500);
        return temp.Text;
      }

      return null;
    }

    protected abstract string get_forecast();
    protected abstract string get_current();
  }
}
