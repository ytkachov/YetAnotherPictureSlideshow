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

      Log.Information("Args: {ArgCount} Folder: [{Folder}] Type: [{Type}] Nocheck: [{Nocheck}]", args.Length, _folder, _type, nocheck);
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

      // Note: WeatherCollector used to Process.Kill stray chrome/edge and
      // driver executables before starting because WeatherSeleniumReader
      // would leak them on exceptions. WeatherSeleniumReader now implements
      // IDisposable and is consumed via using below, so that workaround was
      // removed; killing the user's running browsers as a side effect of
      // collecting weather was hostile.

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
      else if (_type == WeatherSource.YAC)
      {
        Log.Information("Yandex API reader/writer");

        string yak = GetYandexWeatherApiKey();
        if (string.IsNullOrEmpty(yak))
          Log.Error("Yandex API key not found");
        else
        {
          var rw = new YandexApiReaderWriter(yak);
          reader = rw;
          writer = rw;
        }
      }

      if (writer != null && reader != null)
      {
        try
        {
          string temp = "", current = "", forecast = "", except = "";
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
        }
        finally
        {
          // Ensure the Selenium WebDriver / HttpClient / NSU temperature
          // reader inside the reader+writer are released even if writeinfo
          // throws. Without this any exception above left the chromedriver
          // process running until the user manually killed it.
          (reader as IDisposable)?.Dispose();
          if (writer is IDisposable wd && !ReferenceEquals(writer, reader))
            wd.Dispose();
        }
      }

      FinitLOG();
    }

    public static string GetYandexWeatherApiKey()
    {
      string yandexApiKey = "";
      RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\PictureSlideshowScreensaver");
      if (key != null)
      {
        yandexApiKey = (string)key.GetValue("YandexApiKey", "");
      }

      return yandexApiKey;
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
