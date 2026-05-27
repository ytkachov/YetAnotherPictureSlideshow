using System;
using System.Globalization;
using Microsoft.Win32;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.IO;

namespace PictureSlideshowScreensaver.Models
{
  public class Settings
  {
    public string _path = null;
    public double _updateInterval = 5; // seconds
    public int _fadeSpeed = 200;       // milliseconds
    public int _startOffset = 0;
    public int _photosPerFolder = 10;
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

    // Stage 5: provider id and API key are read from Registry here and
    // passed into WeatherOptions at composition time. WeatherProvider
    // matches one of the IWeatherProvider.Name values registered by
    // AddWeatherProviders.
    public string WeatherProvider = "open-meteo";
    public string YandexApiKey = null;

    // Minutes between weather polls. Default 60 — the Yandex free tier
    // caps free accounts at ~30 requests/day, so 1/hour is the natural
    // budget. Registry override is "WeatherPollingMinutes". Clamped
    // [1, 1440] when read so a typo can't accidentally hammer the API
    // or freeze the widget for days.
    public int WeatherPollingMinutes = 60;

    // Optional secondary weather provider used by WeatherPollingService
    // as a fallback when the primary loop's latest data ages past
    // 2 × WeatherPollingMinutes. Empty = no secondary (only primary +
    // NSU last-resort temperature remain). When set, the secondary runs
    // on its own cadence (WeatherPollingMinutesSecondary).
    public string WeatherProviderSecondary = "";
    public int WeatherPollingMinutesSecondary = 30;

    // Tiny "OM" / "Я" / "НГУ" chip overlaid on the live weather tile
    // showing which tier is currently driving the displayed snapshot.
    // 1 = visible (default), 0 = hidden.
    public bool WeatherShowProviderBadge = true;

    // Serilog minimum level for the configured file sinks. Defaults to
    // Verbose to preserve existing behaviour; the Configuration window
    // lets the user dial it down (Information / Warning) once they're
    // happy the slideshow is stable and don't want gigabytes of logs.
    public LogEventLevel _logLevel = LogEventLevel.Verbose;

    // Live, hot-reloadable minimum level. ConfigureFileLogger wires the
    // Serilog pipeline through `.MinimumLevel.ControlledBy(this)` so a
    // mutation here takes effect on the very next log call — that's what
    // the L-key log viewer's level ComboBox flips. The Configuration
    // window's Save also updates this switch (and persists to Registry
    // for the cross-restart default); both paths share one instance so
    // they stay coherent.
    public readonly LoggingLevelSwitch LogLevelSwitch = new(LogEventLevel.Verbose);

    private const string RegistryPath = "SOFTWARE\\PictureSlideshowScreensaver";

    enum PerfOptions
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
      using RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath);
      if (key == null)
        return;

      _path = (string)key.GetValue("ImageFolder");
      _updateInterval = ReadDouble(key, "Interval", _updateInterval);
      _fadeSpeed = ReadInt(key, "FadeTime", _fadeSpeed);
      _photosPerFolder = Math.Max(1, ReadInt(key, "PhotosPerFolder", _photosPerFolder));
      _writeStat = ReadInt(key, "WriteStat", 0) == 1;
      _writeStatPath = (string)key.GetValue("WriteStatFolder");
      _writeLog = ReadInt(key, "WriteLog", 0) == 1;
      _writeLogPath = (string)key.GetValue("WriteLogFolder");
      _dependOnBattery = ReadInt(key, "DependOnBattery", 0) == 1;

      int dflt = (int)(PerfOptions.work_at_night | PerfOptions.no_night_image_accents | PerfOptions.no_night_image_fading | PerfOptions.no_night_image_scaling);
      int po = (int?)key.GetValue("PerformanceOptions") ?? dflt;
      _workAtNight = (po & (int)PerfOptions.work_at_night) != 0;
      _noImageFading = (po & (int)PerfOptions.no_image_fading) != 0;
      _noImageScaling = (po & (int)PerfOptions.no_image_scaling) != 0;
      _noImageAccents = (po & (int)PerfOptions.no_image_accents) != 0;
      _noNightImageFading = (po & (int)PerfOptions.no_night_image_fading) != 0;
      _noNightImageScaling = (po & (int)PerfOptions.no_night_image_scaling) != 0;
      _noNightImageAccents = (po & (int)PerfOptions.no_night_image_accents) != 0;

      // Stage 5 wiring: optional provider override + API key. Both fall
      // back to defaults; an empty/missing string keeps the default and
      // a bad key surfaces as a logged 401/403 from YandexApiWeatherProvider.
      var providerRaw = (string)key.GetValue("WeatherProvider");
      if (!string.IsNullOrWhiteSpace(providerRaw))
        WeatherProvider = providerRaw.Trim();
      YandexApiKey = (string)key.GetValue("YandexApiKey");
      WeatherPollingMinutes = Math.Clamp(ReadInt(key, "WeatherPollingMinutes", WeatherPollingMinutes), 1, 1440);

      var secondaryRaw = (string)key.GetValue("WeatherProviderSecondary");
      if (!string.IsNullOrWhiteSpace(secondaryRaw))
        WeatherProviderSecondary = secondaryRaw.Trim();
      WeatherPollingMinutesSecondary = Math.Clamp(ReadInt(key, "WeatherPollingMinutesSecondary", WeatherPollingMinutesSecondary), 1, 1440);
      WeatherShowProviderBadge = ReadInt(key, "WeatherShowProviderBadge", WeatherShowProviderBadge ? 1 : 0) != 0;

      var logLevelRaw = (string)key.GetValue("LogLevel");
      if (!string.IsNullOrWhiteSpace(logLevelRaw) &&
          Enum.TryParse<LogEventLevel>(logLevelRaw, ignoreCase: true, out var parsedLevel))
        _logLevel = parsedLevel;
      LogLevelSwitch.MinimumLevel = _logLevel;

      EnsureDirectoryExists(_writeStat, _writeStatPath);
      EnsureDirectoryExists(_writeLog, _writeLogPath);

      if (_writeLog && !string.IsNullOrEmpty(_writeLogPath) && Directory.Exists(_writeLogPath))
        ConfigureFileLogger(_writeLogPath, LogLevelSwitch);
    }

    private static int ReadInt(RegistryKey key, string name, int fallback)
    {
      var raw = (string)key.GetValue(name);
      if (raw == null)
        return fallback;
      if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        return parsed;

      Log.Warning("Registry value {Name}={Raw} is not a valid integer; falling back to {Fallback}", name, raw, fallback);
      return fallback;
    }

    private static double ReadDouble(RegistryKey key, string name, double fallback)
    {
      var raw = (string)key.GetValue(name);
      if (raw == null)
        return fallback;
      if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        return parsed;

      Log.Warning("Registry value {Name}={Raw} is not a valid number; falling back to {Fallback}", name, raw, fallback);
      return fallback;
    }

    private static void EnsureDirectoryExists(bool enabled, string path)
    {
      if (!enabled || string.IsNullOrEmpty(path))
        return;

      try
      {
        if (!Directory.Exists(path))
          Directory.CreateDirectory(path);
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "Could not create directory {Path}", path);
      }
    }

    private static void ConfigureFileLogger(string folder, LoggingLevelSwitch levelSwitch)
    {
      var info_log_file = Path.Combine(folder, "information_log-.txt");
      var verbose_log_file = Path.Combine(folder, "verbose_log-.txt");
      var warning_log_file = Path.Combine(folder, "warning_log-.txt");
      var error_log_file = Path.Combine(folder, "error_log-.txt");

      const string output_template = "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext} (at {ClassName} class in {MethodName} method): {Message}{NewLine}{Exception}";

      // shared: true opens each file with FileShare.ReadWrite and writes
      // via Serilog's SharedFileSink (length-positioned appends). Without
      // it the default FileShare.Read leaves the file locked against any
      // outside writer — which is exactly why the L-viewer's Clear button
      // failed to truncate ("file is in use"). The extra per-write flush
      // SharedFileSink does is negligible at our log volume; the
      // flushToDiskInterval hint is ignored in shared mode but kept on
      // the call site for documentation.
      Log.Logger = new LoggerConfiguration()
          .MinimumLevel.ControlledBy(levelSwitch)
          .WriteTo.Async(a => a.File(verbose_log_file, outputTemplate: output_template, flushToDiskInterval: TimeSpan.FromSeconds(10), rollingInterval: RollingInterval.Day, shared: true))
          .WriteTo.Async(a => a.File(info_log_file, outputTemplate: output_template, restrictedToMinimumLevel: LogEventLevel.Information, flushToDiskInterval: TimeSpan.FromSeconds(10), rollingInterval: RollingInterval.Day, shared: true))
          .WriteTo.Async(a => a.File(warning_log_file, outputTemplate: output_template, restrictedToMinimumLevel: LogEventLevel.Warning, flushToDiskInterval: TimeSpan.FromSeconds(10), rollingInterval: RollingInterval.Day, shared: true))
          .WriteTo.Async(a => a.File(error_log_file, outputTemplate: output_template, restrictedToMinimumLevel: LogEventLevel.Error, flushToDiskInterval: TimeSpan.FromSeconds(1), rollingInterval: RollingInterval.Day, shared: true))
          .CreateLogger()
          .ForContext<App>();
    }
  }
}
