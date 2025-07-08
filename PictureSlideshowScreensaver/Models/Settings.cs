using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using Serilog.Events;
using Serilog;
using System.IO;

namespace PictureSlideshowScreensaver.Models
{
  class Settings
  {
    public string _path = null;
    public double _updateInterval = 5; // seconds
    public int _fadeSpeed = 200;       // milliseconds
    public int _startOffset = 0;
    public bool _writeStat = false;
    public string _writeStatPath;
    public bool _writeLog = false;
    public string _writeLogPath;
    public bool _dependOnBattery = false;
    public bool _workAtNight = true;
    public bool _noImageFading = false;
    public bool _noImageScaling = false;
    public bool _noImageAccents = false;
    public bool _noNightImageFading = true;
    public bool _noNightImageScaling = true;
    public bool _noNightImageAccents = true;

    enum perfoptions
    {
      work_at_night = 0x0001,
      no_image_fading = 0x0002,
      no_image_scaling = 0x0004,
      no_image_accents = 0x0008,
      no_night_image_fading = 0x0020,
      no_night_image_scaling = 0x0040,
      no_night_image_accents = 0x0080
    }

    public Settings()
    {
      RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\PictureSlideshowScreensaver");
      if (key != null)
      {
        _path = (string)key.GetValue("ImageFolder");
        _updateInterval = double.Parse((string)key.GetValue("Interval") ?? "10.0");
        _fadeSpeed = int.Parse((string)key.GetValue("FadeTime") ?? "1000");
        _writeStat = int.Parse((string)key.GetValue("WriteStat") ?? "0") == 1;
        _writeStatPath = (string)key.GetValue("WriteStatFolder");
        _writeLog = int.Parse((string)key.GetValue("WriteLog") ?? "0") == 1;
        _writeLogPath = (string)key.GetValue("WriteLogFolder");
        _dependOnBattery = int.Parse((string)key.GetValue("DependOnBattery") ?? "0") == 1;

        int dflt = (int)(perfoptions.work_at_night | perfoptions.no_night_image_accents | perfoptions.no_night_image_fading | perfoptions.no_night_image_scaling);
        int po = (int?)key.GetValue("PerformanceOptions") ?? dflt;
        _workAtNight = (po & (int)perfoptions.work_at_night) != 0;
        _noImageFading = (po & (int)perfoptions.no_image_fading) != 0;
        _noImageScaling = (po & (int)perfoptions.no_image_scaling) != 0;
        _noImageAccents = (po & (int)perfoptions.no_image_accents) != 0;
        _noNightImageFading = (po & (int)perfoptions.no_night_image_fading) != 0;
        _noNightImageScaling = (po & (int)perfoptions.no_night_image_scaling) != 0;
        _noNightImageAccents = (po & (int)perfoptions.no_night_image_accents) != 0;

        if (_writeStat)
          if (!Directory.Exists(_writeStatPath))
            Directory.CreateDirectory(_writeStatPath);

        if (_writeLog)
        {
          if (!Directory.Exists(_writeLogPath))
            Directory.CreateDirectory(_writeLogPath);

          if (Directory.Exists(_writeLogPath))
          {
            var info_log_file = Path.Combine(_writeLogPath, "information_log-.txt");
            var verbose_log_file = Path.Combine(_writeLogPath, "verbose_log-.txt");
            var warning_log_file = Path.Combine(_writeLogPath, "warning_log-.txt");
            var error_log_file = Path.Combine(_writeLogPath, "error_log-.txt");

            string output_template = "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext} (at {ClassName} class in {MethodName} method): {Message}{NewLine}{Exception}";
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose() // Set minimum log level
                .WriteTo.Async(a => a.File(verbose_log_file, outputTemplate: output_template, flushToDiskInterval: TimeSpan.FromSeconds(10), rollingInterval: RollingInterval.Day)) // Log to file
                .WriteTo.Async(a => a.File(info_log_file, outputTemplate: output_template, restrictedToMinimumLevel: LogEventLevel.Information, flushToDiskInterval: TimeSpan.FromSeconds(10), rollingInterval: RollingInterval.Day)) // Log to file
                .WriteTo.Async(a => a.File(warning_log_file, outputTemplate: output_template, restrictedToMinimumLevel: LogEventLevel.Warning, flushToDiskInterval: TimeSpan.FromSeconds(10), rollingInterval: RollingInterval.Day)) // Log to file
                .WriteTo.Async(a => a.File(error_log_file, outputTemplate: output_template, restrictedToMinimumLevel: LogEventLevel.Error, flushToDiskInterval: TimeSpan.FromSeconds(1), rollingInterval: RollingInterval.Day)) // Log to file
                .CreateLogger()
                .ForContext<App>();
          }
        }

      }
    }
  }
}
