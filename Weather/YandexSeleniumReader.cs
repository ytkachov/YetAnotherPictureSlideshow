using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenQA.Selenium;

namespace weather
{
  public class YandexSeleniumReader : WeatherSeleniumReader
  {
    // for Novosibirsk by default
    public YandexSeleniumReader(WeatherSource type, double lat = 54.85194397, double lon = 83.10189056) : base(type)
    {
      SetLocation(lat, lon);
    }

    public void SetLocation(double lat, double lon)
    {
      _weather_url = $"https://yandex.ru/pogoda/?lat={lat}&lon={lon}";
      _weather_forecast_url = $"https://yandex.ru/pogoda/details?lat={lat}&lon={lon}&via=ms#{DateTime.Now.Day}";
    }

    protected override string get_forecast()
    {
      var cards = _driver.findElements(By.XPath("//div[@class='card']"));

      if (cards == null)
        return null;

      string result = "<forecast>";
      for (int i = 0; i < cards.Count; i++)
      {
        var card = cards[i];
        result += "\n" + _driver.correctOuterHTML(card);
      }

      result += "\n</forecast>";
      return result;
    }

    protected override string get_current()
    {
      string result = "<current>\n";
      var info = _driver.findElement(By.XPath("//div[@class='fact__temp-wrap']"));
      if (info != null)
       result += _driver.correctOuterHTML(info);

      info = _driver.findElement(By.XPath("//div[@class='fact__props']"));
      if (info != null)
        result += "\n\n" + _driver.correctOuterHTML(info);

      var divs = _driver.findElements(By.XPath("//div[@class='content__top']//span[@class='fact__hour-elem']"));
      foreach (var div in divs)
        result += "\n" + _driver.correctOuterHTML(div);

      result += "\n</current>";
      return result;
    }

  }

}
