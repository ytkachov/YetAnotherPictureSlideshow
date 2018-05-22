using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using weather;
namespace WeatherCollector
{

  class WeatherCollectorApp
  {
    private static string _folder = ".";
    private static int _type = 0;

    [STAThread]
    static void Main(string[] args)
    {
      if (args.Length > 0)
        _folder = args[0];

      if (args.Length > 1)
        _type = int.Parse(args[1]);

      NGSFileReader writer = new NGSFileReader(_folder);

      INGSWeatherReader reader;
      if (_type == 0)
        reader = new NGSWatinReader();
      else
        reader = new NGSSeleniumReader();

      reader.navigate();
      string temp = reader.temperature();

      reader.navigate("http://pogoda.ngs.ru/academgorodok/");
      string current = reader.current();
      string forecast = reader.forecast();

      writer.writeinfo(temp, current, forecast);

      writer.close();
      reader.close();
    }
  }
}
