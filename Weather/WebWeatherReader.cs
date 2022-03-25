using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.IE;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Support.UI;
using WatiN.Core;
using System.Collections.ObjectModel;

namespace weather
{
  public enum WeatherSource
  {
    NW = 0, // NGS Watin 
    NC = 1, // NGS Chrome
    NI = 2, // NGS IE
    NE = 3, // NGS Edge
    YC = 4, // Yandex Chrome
    YI = 5, // Yandex IE
    YE = 6, // Yandex Edge
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

  static class WatinExtensions
  {
    public static DivCollection ChildDivs(this Element self)
    {
      return self.DomContainer.Divs.Filter(e => self.Equals(e.Parent));
    }
    public static ElementCollection EChildDivs(this Element self)
    {
      return self.DomContainer.Elements.Filter(e => (self.Equals(e.Parent) && e.TagName.ToLower() == "div"));
    }
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
    //// str - the source string
    //// index- the start location to replace at (0-based)
    //// length - the number of characters to be removed before inserting
    //// replace - the string that is replacing characters
    public static string ReplaceAt(this string str, int index, int length, string replace)
    {
      return str.Remove(index, Math.Min(length, str.Length - index))
              .Insert(index, replace);
    }

    public static string correctOuterHTML(this IWebDriver driver, IWebElement element)
    {
      string outerhtml = driver.outerHTML(element);

      // correct incorrect <img > tags
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
      catch (Exception e)
      {
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
      catch (Exception e)
      {
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

  public class NGSWatinReader : IWeatherReader
  {
    private IE _browser;
    private static string _weather_url = "https://pogoda.ngs.ru/academgorodok/";

    public NGSWatinReader()
    {
      Settings.AutoMoveMousePointerToTopLeft = false;
      //Settings.MakeNewIeInstanceVisible = false;

      if (_browser == null)
        _browser = new IE();
    }

    public void close()
    {
      if (_browser != null)
        _browser.Close();

      _browser = null;
    }

    public string current()
    {
      navigate(_weather_url);
      Thread.Sleep(10000);

      string outerhtml;
      Div info = _browser.Div(Find.ByClass("today-panel__info"));
      if (!info.Exists)
      {
        outerhtml = _browser.Html;

        File.WriteAllText(@"D:\outerhtml.htm", outerhtml);

        throw new Exception("incorrect current weather structure ");
      }

      outerhtml = info.OuterHtml.Replace("&nbsp;", " ");

      // remove usually incorrect <img > tags
      string img = "<img\\s[^>]*?src\\s*=\\s*['\\\"]([^ '\\\"]*?)['\\\"][^>]*?>";

      return Regex.Replace(outerhtml, img, " ");
    }

    public string forecast()
    {
      // navigate(_weather_url);
      //Thread.Sleep(10000);

      Table tbl = _browser.Table(Find.ByClass("pgd-detailed-cards elements"));
      if (!tbl.Exists)
        tbl = _browser.Table(Find.ByClass("pgd-detailed-cards elements pgd-hidden"));

      if (!tbl.Exists)
        throw new Exception("NGS forecast: can't find 3 day forecast table");

      string outerhtml = tbl.OuterHtml.Replace("&nbsp;", " ");

      return outerhtml;
    }

    public void getrest()
    {
      _browser.GoTo("http://google.com/");
    }

    private void navigate(string url = null)
    {
      if (_browser == null)
        _browser = new IE();

      if (url == null)
        _browser.GoTo("http://weather.nsu.ru/");
      else
        _browser.GoToNoWait(url);
    }

    public void restart()
    {
      close();
    }

    public string temperature()
    {
      navigate();
      Thread.Sleep(1000);

      Span temp = _browser.Span(Find.ById("temp"));
      if (temp.Exists)
      {
        Thread.Sleep(500);
        return temp.Text;
      }

      return null;
    }
  }

  public abstract class WeatherSeleniumReader : IWeatherReader
  {
    protected IWebDriver _driver = null;
    protected WeatherSource _type;
    protected string _weather_url;
    protected string _weather_forecast_url;

    public WeatherSeleniumReader(WeatherSource type)
    {
      _type = type;
      _driver = create_driver();
    }

    public void close()
    {
      try
      {
        _driver.Close();
        _driver.Quit();
      }
      catch (Exception e)
      {

      }

      _driver = null;
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
      _driver.Close();
      _driver.Quit();

      _driver = null;
    }

    public void getrest()
    {
      _driver.Navigate().GoToUrl("http://google.com/");
    }

    protected virtual IWebDriver create_driver()
    {
      IWebDriver driver = null;
      if (_type == WeatherSource.NI || _type == WeatherSource.YI)
        driver = new InternetExplorerDriver();
      else if (_type == WeatherSource.NE || _type == WeatherSource.YE)
        driver = new EdgeDriver();
      else if (_type == WeatherSource.NC || _type == WeatherSource.YC)
        driver = new ChromeDriver();

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
          _driver.Navigate().GoToUrl("http://weather.nsu.ru/");
        else
          _driver.Navigate().GoToUrl(url);
      }
      catch (Exception e)
      {
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
