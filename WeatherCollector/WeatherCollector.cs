using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using Serilog;
using Serilog.Events;
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
      InitLOG();

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

      Log.Information($"Args: {args.Length} Folder: [{_folder}] Type: [{_type}] Nocheck: [{nocheck}] Nokill: [{nokill}]");
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
        {
          Log.Information("WeatherCollector exit: no mather app working");
          FinitLOG();

          return;
        }
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


      IWeatherWriter writer = null;
      IWeatherReader reader = null;
      if (_type == WeatherSource.NI || _type == WeatherSource.NC || _type == WeatherSource.NE)
      {
        Log.Information("NGS selenium reader");

        reader = new NGSSeleniumReader(_type); 
        writer = new NGSFileReaderWriter(_type);
      }
      else if (_type == WeatherSource.YI || _type == WeatherSource.YC || _type == WeatherSource.YE)
      {
        Log.Information("Yandex selenium reader");

        reader = new YandexSeleniumReader(_type);    
        writer = new YandexFileReaderWriter(_type);
      }

      string temp = "", current = "", forecast = "", except="";
      try
      {
        temp = reader.temperature();
      }
      catch (Exception ex)
      {
        Log.Error(ex, "");

        except += "\n\n\n ======================= \n" + ex.Message;
      }

      try
      {
        current = reader.current();
      }
      catch (Exception ex)
      {
        Log.Error(ex, "");
        except += "\n\n\n ======================= \n" + ex.Message;
      }

      try
      {
        forecast = reader.forecast();
      }
      catch (Exception ex)
      {
        Log.Error(ex, "");
        except += "\n\n\n ======================= \n" + ex.Message;
      }

      writer.writeinfo(temp, current, forecast, except);
      reader.close();

      FinitLOG();
    }

    public static void InitLOG()
    {
      RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\PictureSlideshowScreensaver");
      if (key != null)
      {
        bool writeLog = int.Parse((string)key.GetValue("WriteLog") ?? "0") == 1;
        string writeLogPath = (string)key.GetValue("WriteLogFolder");

        if (writeLog)
        {
          if (!Directory.Exists(writeLogPath))
            Directory.CreateDirectory(writeLogPath);

          if (Directory.Exists(writeLogPath))
          {
            var info_log_file = Path.Combine(writeLogPath, "wc_information_log-.txt");
            var verbose_log_file = Path.Combine(writeLogPath, "wc_verbose_log-.txt");
            var warning_log_file = Path.Combine(writeLogPath, "wc_warning_log-.txt");
            var error_log_file = Path.Combine(writeLogPath, "wc_error_log-.txt");

            string output_template = "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext} (at {ClassName} class in {MethodName} method): {Message}{NewLine}{Exception}";
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose() // Set minimum log level
                .WriteTo.Async(a => a.File(verbose_log_file, outputTemplate: output_template, flushToDiskInterval: TimeSpan.FromSeconds(10), rollingInterval: RollingInterval.Day)) // Log to file
                .WriteTo.Async(a => a.File(info_log_file, outputTemplate: output_template, restrictedToMinimumLevel: LogEventLevel.Information, flushToDiskInterval: TimeSpan.FromSeconds(10), rollingInterval: RollingInterval.Day)) // Log to file
                .WriteTo.Async(a => a.File(warning_log_file, outputTemplate: output_template, restrictedToMinimumLevel: LogEventLevel.Warning, flushToDiskInterval: TimeSpan.FromSeconds(10), rollingInterval: RollingInterval.Day)) // Log to file
                .WriteTo.Async(a => a.File(error_log_file, outputTemplate: output_template, restrictedToMinimumLevel: LogEventLevel.Error, flushToDiskInterval: TimeSpan.FromSeconds(1), rollingInterval: RollingInterval.Day)) // Log to file
                .CreateLogger()
                .ForContext<WeatherCollectorApp>();
          }
        }
      }

      Log.Information("Start weather collector");
    }

    public static void FinitLOG()
    {
      Log.Information("Finish weather collector");
      Log.CloseAndFlush();
    }
  }
}
