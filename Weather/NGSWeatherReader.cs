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
using OpenQA.Selenium.Support.UI;
using WatiN.Core;

namespace weather
{
  public interface INGSWeatherReader
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

  public class NGSWatinReader : INGSWeatherReader
  {
    private IE _browser;
    private static string _weather_url = "https://pogoda.ngs.ru/academgorodok/";

    private class StringStartsWith : WatiN.Core.Comparers.Comparer<string>
    {
      public StringStartsWith()
      {
      }

      public string startswith { get; set; }

      public override bool Compare(string V)
      {
        return V != null && V.StartsWith(startswith);
      }
    }

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

  public class NGSSeleniumReader : INGSWeatherReader
  {
    private IWebDriver _driver = null;
    private static string _weather_url = "https://pogoda.ngs.ru/academgorodok/";

    public NGSSeleniumReader()
    {
      _driver = new ChromeDriver();
      _driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(20);
    }

    public void close()
    {
      _driver.Close();
      _driver.Quit();

      _driver = null;
    }

    public string current()
    {
      navigate(_weather_url);

      // var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(20));
      //var info = wait.Until((d) => { return d.findElement(By.ClassName("today-panel__info")); });
      var info = _driver.findElement(By.ClassName("today-panel__info"));
      if (info == null)
        return null;

      string outerhtml = _driver.outerHTML(info).Replace("&nbsp;", " ");

      // remove usually incorrect <img > tags
      string img = "<img\\s[^>]*?src\\s*=\\s*['\\\"]([^ '\\\"]*?)['\\\"][^>]*?>";
      return Regex.Replace(outerhtml, img, " ");
    }

    public string forecast()
    {
      if (_driver.Url != _weather_url)
      {
        navigate(_weather_url);
      }

      // var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(22));
      // var tbl = wait.Until((d) => { return d.findElement(By.XPath("//table[@class='pgd-detailed-cards elements']")); });
      var tbl = _driver.findElement(By.XPath("//table[@class='pgd-detailed-cards elements']"));
      if (tbl == null)
      {
        // wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(24));
        // tbl = wait.Until((d) => { return d.findElement(By.XPath("//table[@class='pgd-detailed-cards elements pgd-hidden']")); });
        tbl = _driver.findElement(By.XPath("//table[@class='pgd-detailed-cards elements pgd-hidden']"));
      }

      if (tbl == null)
        return null;

      string outerhtml = _driver.outerHTML(tbl).Replace("&nbsp;", " ");
      return outerhtml;
     }

    private void navigate(string url = null)
    {
      
      if (_driver == null)
      {
        _driver = new ChromeDriver();
        _driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(20);
      }

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

    public void restart()
    {
      _driver.Close();
      _driver.Quit();

      _driver = null;
    }

    public string temperature()
    {
      navigate();
      Thread.Sleep(1000);

      var temp = _driver.findElement(By.Id("temp"));
      if (temp != null)
      {
        Thread.Sleep(500);
        return temp.Text;
      }

      return null;
    }

    public void getrest()
    {
      _driver.Navigate().GoToUrl("http://google.com/");
    }
  }
}
