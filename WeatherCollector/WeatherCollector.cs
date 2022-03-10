using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using weather;
namespace WeatherCollector
{

  class WeatherCollectorApp
  {
    private static string _folder = ".";
    private static WeatherSource _type = WeatherSource.NC;

    [STAThread]
    static void Main(string[] args)
    {
      if (args.Length > 0)
        _folder = args[0];

      if (args.Length > 1)
        _type = (WeatherSource)int.Parse(args[1]);

      bool nocheck = false;
      if (args.Length > 2)
        nocheck = true;

      bool nokill = false;
      if (args.Length > 3)
        nokill = true;

      if (!nocheck)
      {
        // check if mother app is running
        Process[] pl = Process.GetProcesses();
        bool running = false;
        foreach (var p in pl)
        {
          if (p.ProcessName.Equals("PictureSlideshowScreensaver", StringComparison.OrdinalIgnoreCase))
          {
            running = true;
            break;
          }
        }

        if (!running)
          return;
      }

      // nokill = true;
      if (!nokill)
      {
        string browsername = "chrome";
        string drivername = "chromedriver";
        if (_type == WeatherSource.NI || _type == WeatherSource.YI)
        {
          browsername = "iexplore";
          drivername = "IEDriverServer";
        }
        else if (_type == WeatherSource.NE || _type == WeatherSource.YE)
        {
          browsername = "edge";
          drivername = "msedgeDriver";
        }

        Process[] pl = Process.GetProcesses();

        foreach (var p in pl)
        {
          string mwt = p.MainWindowTitle;
          string pn = p.ProcessName;
          if (p.ProcessName.Equals(browsername, StringComparison.OrdinalIgnoreCase))
          {
            //p.CloseMainWindow();
            try
            {
              p.Kill();
            }
            catch (Exception)
            {
            }
          }
        }

        foreach (var p in pl)
        {
          string mwt = p.MainWindowTitle;
          string pn = p.ProcessName;
          if (p.ProcessName.Equals(drivername, StringComparison.OrdinalIgnoreCase))
          {
            p.Kill();
          }
        }
      }


      IWeatherReader writer = null;
      IWeatherReader reader = null;
      if (_type == WeatherSource.NI || _type == WeatherSource.NC || _type == WeatherSource.NE)
      {
        reader = new NGSSeleniumReader(_type); // 1 - chrome; 2 - IE; 3 - Edge;
        writer = new NGSFileReader();
      }
      else if (_type == WeatherSource.YI || _type == WeatherSource.YC || _type == WeatherSource.YE)
      {
        reader = new YandexSeleniumReader(_type); // 1 - chrome; 2 - IE; 3 - Edge;
        writer = new YandexFileReader();
      }

      string temp = "", current = "", forecast = "", except="";
      try
      {
        temp = reader.temperature();
      }
      catch (Exception e)
      {
        except += "\n\n\n ======================= \n" + e.Message;
      }

      try
      {
        current = reader.current();
      }
      catch (Exception e)
      {
        except += "\n\n\n ======================= \n" + e.Message;
      }

      try
      {
        forecast = reader.forecast();
      }
      catch (Exception e)
      {
        except += "\n\n\n ======================= \n" + e.Message;
      }

      writer.writeinfo(temp, current, forecast, except);

      writer.close();
      reader.close();
    }
  }
}
