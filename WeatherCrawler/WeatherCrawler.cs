using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using weather;

namespace WeatherCrawler
{

  internal class WeatherCrawlerApp
  {
    [STAThread]
    static void Main(string[] args)
    {
      YandexSeleniumReader reader = new YandexSeleniumReader(WeatherSource.YC);
      //var reader = new YandexFileReaderWriter(WeatherSource.YC);
      var yandex = new YandexWeatherExtractor(reader);

      var w = new WeatherInfo();
      yandex.get_current_weather(w);

      var forecast = new Dictionary<WeatherPeriod, WeatherInfo>();
      yandex.get_forecast(forecast);

      //for (double lat = -80.03; lat < 0; lat += 3.018)
      //{
      //  for (double lon = -180.02; lon < 170; lon += 3.046)
      //  {
      //    reader.SetLocation(lat, lon);

      //    yandex.get_current_weather(new WeatherInfo());
      //    yandex.get_forecast(new Dictionary<WeatherPeriod, WeatherInfo>());
      //  }
      //}


      //yandex.get_nsu_current_temp(w);

      reader.close();
    }
  }
}
