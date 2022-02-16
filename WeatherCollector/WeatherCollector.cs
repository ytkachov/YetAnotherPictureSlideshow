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
    private static int _type = 0;

    [STAThread]
    static void Main(string[] args)
    {
      if (args.Length > 0)
        _folder = args[0];

      if (args.Length > 1)
        _type = int.Parse(args[1]);

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

      if (!nokill)
      {
        Process[] pl = Process.GetProcesses();

        foreach (var p in pl)
        {
          string mwt = p.MainWindowTitle;
          string pn = p.ProcessName;
          if (p.ProcessName.Equals("chrome", StringComparison.OrdinalIgnoreCase))
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
          if (p.ProcessName.Equals("chromedriver", StringComparison.OrdinalIgnoreCase))
          {
            p.Kill();
          }
        }
      }


      NGSFileReader writer = new NGSFileReader(_folder);
      INGSWeatherReader reader;
      if (_type == 1)
        reader = new NGSWatinReader();
      else
        reader = new NGSSeleniumReader();

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
