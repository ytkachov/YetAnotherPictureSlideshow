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
      _weather_forecast_url = $"https://yandex.ru/pogoda/details?lat=54.85194397&lon=83.10189056&via=ms#{dayofmonth}";
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
        result += "\n" + _driver.outerHTML(card).Replace("<br>", " ").Replace("</br>", " ");
      }

      result += "\n</forecast>";
      return result;
    }

    protected override string get_current()
    {
      string result = "<body>\n";
      var info = _driver.findElement(By.XPath("//div[@class='fact__temp-wrap']"));
      if (info != null)
       result = _driver.outerHTML(info);

      info = _driver.findElement(By.XPath("//div[@class='fact__props']"));
      if (info != null)
        result += "\n\n" + _driver.outerHTML(info);

      info = _driver.findElement(By.XPath("//div[@class='swiper-container fact__hourly-swiper']"));
      if (info != null)
        result += "\n\n" + _driver.outerHTML(info);

      result += "\n</body>";
      return result;
    }

  }

}
