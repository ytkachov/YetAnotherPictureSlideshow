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

      string temp = "", current = "", forecast = "";
      try
      {
        temp = reader.temperature();
      }
      catch (Exception e)
      {
      }

      try
      {
        current = reader.current();
      }
      catch (Exception e)
      {
      }

      try
      {
        forecast = reader.forecast();
      }
      catch (Exception e)
      {
      }

      writer.writeinfo(temp, current, forecast);

      writer.close();
      reader.close();
    }
  }
}
