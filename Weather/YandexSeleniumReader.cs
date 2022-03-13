using System;
using System.Text.RegularExpressions;
using OpenQA.Selenium;

namespace weather
{
  public class YandexSeleniumReader : WeatherSeleniumReader
  {
    public YandexSeleniumReader(WeatherSource type) : base(type)
    {
      _weather_url = "https://yandex.ru/pogoda/?lat=54.85194397&lon=83.10189056";
      
      int dayofmonth = DateTime.Now.Day;
      _weather_forecast_url = $"https://yandex.ru/pogoda/?lat=54.85194397&lon=83.10189056&via=ms#{dayofmonth}";
    }

    protected override string get_forecast()
    {
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

    protected override string get_current()
    {
      string result = "<body>\n";
      var info = _driver.findElement(By.ClassName("fact__temp-wrap"));
      if (info != null)
       result = _driver.outerHTML(info);

      info = _driver.findElement(By.ClassName("fact__props"));
      if (info != null)
        result += "\n\n" + _driver.outerHTML(info);

      info = _driver.findElement(By.ClassName("swiper-container fact__hourly-swiper"));
      if (info != null)
        result += "\n\n" + _driver.outerHTML(info);

      result += "\n</body>";
      return result;
    }

  }

}
