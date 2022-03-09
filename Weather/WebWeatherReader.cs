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

namespace weather
{
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
    protected int _type = 0;
    protected string _weather_url;

    public WeatherSeleniumReader(int type)
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

    public string current()
    {
      navigate(_weather_url);
      return get_current();
    }

    public string forecast()
    {
      if (_driver.Url != _weather_url)
        navigate(_weather_url);

      return get_forecast();
    }

    public string temperature()
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
      if (_type == 2)
        driver = new InternetExplorerDriver();
      else if (_type == 3)
        driver = new EdgeDriver();
      else
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
