﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Text.RegularExpressions;
using OpenQA.Selenium;


namespace weather
{
  public class NGSSeleniumReader : WeatherSeleniumReader
  {
    public NGSSeleniumReader(WeatherSource type) : base(type)
    {
      _weather_url = "https://pogoda.ngs.ru/academgorodok/";
      _weather_forecast_url = _weather_url;
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
      var info = _driver.findElement(By.ClassName("today-panel__info"));
      if (info == null)
        return null;

      string outerhtml = _driver.outerHTML(info).Replace("&nbsp;", " ");

      // remove usually incorrect <img > tags
      string img = "<img\\s[^>]*?src\\s*=\\s*['\\\"]([^ '\\\"]*?)['\\\"][^>]*?>";
      return Regex.Replace(outerhtml, img, " ");
    }
  }
}
