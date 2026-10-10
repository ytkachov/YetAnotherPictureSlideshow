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

    // Folders holding long-form video (home movies, edited films), as a
    // ';'-separated list like ImageFolder; each is searched with its
    // subfolders, and scanned even when it lies outside ImageFolder. Videos
    // under these play in slices from one shared virtual folder (see
    // VideoChunkSeconds / VideoFolderWeight). Every other video in the library
    // is a short camera clip and plays whole, in turn with the photos of its
    // own folder. Registry key "VideoFolder"; empty = no long-form video.
    public string[] VideoFolders = [];

    // Long-form videos are shown as clips of this many seconds, one clip per
    // visit to the virtual video folder. Registry key "VideoChunkSeconds",
    // clamped [3, 300]. A video shorter than a chunk (plus a third, see
    // LocalImages) is simply shown whole.
    public int VideoChunkSeconds = 15;

    // How strongly the virtual long-form video folder is favoured over its
    // fair share of rotation visits. 1 = strictly proportional to clip count,
    // which for a library of tens of thousands of photos and a couple of
    // minutes of video means a clip roughly every five hours; 10 (the default)
    // brings that to about every half hour. 0 switches long-form video off;
    // short clips in photo folders are unaffected.
    // Registry key "VideoFolderWeight", clamped [0, 1000].
    public int VideoFolderWeight = 10;

    // Whether a photo with an iPhone Live Photo movie next to it (IMG_1.jpg +
    // IMG_1.mov) plays that movie first and then dissolves into the still.
    // 0 shows the photo as a plain still. Live Photo movies whose still is
    // missing never play either way. Registry key "ShowLivePhotoVideos", 0/1.
    public bool ShowLivePhotoVideos = true;

    // Playback volume for video, 0..100. Defaults to silent: a frame on the
    // wall that suddenly starts making noise every half hour is worse than
    // one that doesn't. Registry key "VideoVolume".
    public int VideoVolume = 0;

    // How often the in-memory photo show registry is written to disk.
    // Coarse on purpose — the registry exists to answer "is the rotation
    // even over months", not to survive every second. A flush also happens
    // on clean shutdown, so this only bounds what a power cut costs.
    // Registry override "StatsFlushHours", clamped [1, 168].
    public int StatsFlushHours = 6;

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
      StatsFlushHours = Math.Clamp(ReadInt(key, "StatsFlushHours", StatsFlushHours), 1, 168);
      VideoFolders = ((string)key.GetValue("VideoFolder") ?? "")
          .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      ShowLivePhotoVideos = ReadInt(key, "ShowLivePhotoVideos", ShowLivePhotoVideos ? 1 : 0) == 1;
      VideoChunkSeconds = Math.Clamp(ReadInt(key, "VideoChunkSeconds", VideoChunkSeconds), 3, 300);
      VideoFolderWeight = Math.Clamp(ReadInt(key, "VideoFolderWeight", VideoFolderWeight), 0, 1000);
      VideoVolume = Math.Clamp(ReadInt(key, "VideoVolume", VideoVolume), 0, 100);

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

    /// <summary>
    /// Folder holding the photo show registry (and the report the flush
    /// service writes next to it). Diagnostics belong together, so the
    /// configured log folder wins; the stat folder is the next best (an
    /// appliance may have WriteStat configured but not WriteLog), and
    /// %TEMP%\PictureSlideshow — the same place App.xaml.cs puts its
    /// startup log — is the last resort.
    /// </summary>
    // Command-line override for debugging (/folder:<path>): the library is
    // that folder and its subfolders only, no long-form video folder, and
    // the show registry and logs go to %TEMP%\PictureSlideshow so a test run
    // doesn't touch the real library's statistics.
    public void UseDebugFolder(string folder)
    {
      _path = Path.TrimEndingDirectorySeparator(folder) + @"\*";
      VideoFolders = [];
      _writeStat = false;
      _writeLog = false;
      Log.Information("Debug run: library is {Folder}, registry ImageFolder/VideoFolder ignored", _path);
    }

    public string ResolveStatsFolder()
    {
      if (_writeLog && !string.IsNullOrEmpty(_writeLogPath) && Directory.Exists(_writeLogPath))
        return _writeLogPath;

      if (_writeStat && !string.IsNullOrEmpty(_writeStatPath) && Directory.Exists(_writeStatPath))
        return _writeStatPath;

      return Path.Combine(Path.GetTempPath(), "PictureSlideshow");
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
