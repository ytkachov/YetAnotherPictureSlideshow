using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PictureSlideshowScreensaver.Models;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace PictureSlideshowScreensaver.ViewModels
{
  /// <summary>
  /// View model for the L-key log viewer. Resolves "the active log
  /// folder" the same way the app does on startup (configured WriteLog
  /// folder, falling back to %TEMP%\PictureSlideshow), picks the most
  /// recently modified file in it, and tails the last chunk so the
  /// window stays snappy on multi-megabyte logs.
  /// </summary>
  public partial class LogViewerViewModel : ObservableObject
  {
    // Cap on bytes pulled into the TextBox. Serilog files can run into
    // tens of MB by end of day; pasting all of it into a WPF TextBox
    // makes the editor crawl. The tail is what's useful anyway.
    private const long TailMaxBytes = 256 * 1024;

    private const string RegistryPath = "SOFTWARE\\PictureSlideshowScreensaver";

    private readonly Settings _settings;
    private readonly LoggingLevelSwitch _levelSwitch;

    [ObservableProperty] private string _logPath = "(no log file)";
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private string _statusText = "";

    // Fatal is deliberately omitted — picking it would silence everything
    // the app emits (no code path writes Log.Fatal as its primary signal).
    // Matches ConfigurationViewModel.LogLevels.
    public IReadOnlyList<LogEventLevel> LogLevels { get; } = new[]
    {
        LogEventLevel.Verbose,
        LogEventLevel.Debug,
        LogEventLevel.Information,
        LogEventLevel.Warning,
        LogEventLevel.Error
    };

    [ObservableProperty] private LogEventLevel _selectedLogLevel;

    // Wired by the view: a MessageBox-based error reporter and a
    // confirmation prompt for destructive actions. Kept off the VM so
    // it stays unit-testable without a real Window.
    public Action<string> ShowError { get; set; }
    public Func<bool> ConfirmClear { get; set; }
    public event EventHandler RequestClose;

    public LogViewerViewModel(Settings settings, LoggingLevelSwitch levelSwitch)
    {
      _settings = settings;
      _levelSwitch = levelSwitch;
      _selectedLogLevel = levelSwitch.MinimumLevel;
    }

    // Mutating SelectedLogLevel pokes the live switch immediately AND
    // persists to Registry so the choice survives a restart (matching what
    // the Configuration window does on Save). The Settings._logLevel field
    // is not updated — it's a snapshot of "what we found in registry at
    // startup" and not consulted again after ConfigureFileLogger ran.
    partial void OnSelectedLogLevelChanged(LogEventLevel value)
    {
      if (_levelSwitch is null)
        return;

      _levelSwitch.MinimumLevel = value;
      try
      {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        key.SetValue("LogLevel", value.ToString());
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "Could not persist LogLevel={Level} to Registry", value);
      }
      Log.Information("Log level switched to {Level}", value);
    }

    [RelayCommand]
    private void Refresh()
    {
      var folder = ResolveLogFolder();
      var file = FindMostRecentLog(folder);

      LogPath = file ?? (folder ?? "(no log folder)");

      if (file == null || !File.Exists(file))
      {
        LogText = "(no log file found)";
        StatusText = "";
        return;
      }

      try
      {
        // FileShare.ReadWrite|Delete so we don't conflict with Serilog's
        // writer or its rolling/delete operations during the read.
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read,
                                      FileShare.ReadWrite | FileShare.Delete);
        long total = fs.Length;
        long start = Math.Max(0, total - TailMaxBytes);
        fs.Seek(start, SeekOrigin.Begin);

        using var sr = new StreamReader(fs, Encoding.UTF8);
        var text = sr.ReadToEnd();

        // If we sliced mid-line, drop the partial first line so the
        // viewer doesn't open with broken text.
        if (start > 0)
        {
          int nl = text.IndexOf('\n');
          if (nl >= 0)
            text = text.Substring(nl + 1);
        }

        LogText = text;
        StatusText = FormatStatus(total, text.Length);
      }
      catch (Exception ex)
      {
        LogText = $"(failed to read log: {ex.Message})";
        StatusText = "";
      }
    }

    [RelayCommand]
    private void OpenFolder()
    {
      var folder = ResolveLogFolder();
      if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
      {
        ShowError?.Invoke("Log folder does not exist.");
        return;
      }

      try
      {
        // UseShellExecute is required for explorer.exe association in
        // .NET Core / .NET 8; without it Process.Start would try to
        // exec the path as a binary.
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
      }
      catch (Exception ex)
      {
        ShowError?.Invoke($"Cannot open folder: {ex.Message}");
      }
    }

    [RelayCommand]
    private void Clear()
    {
      var folder = ResolveLogFolder();
      var file = FindMostRecentLog(folder);
      if (file == null)
        return;

      if (ConfirmClear != null && !ConfirmClear())
        return;

      try
      {
        // Truncate-in-place rather than delete: Serilog has the file
        // open with FileShare.Read and a delete would race the writer.
        // Open with the same sharing so we don't lock the writer out.
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Write,
                                      FileShare.ReadWrite | FileShare.Delete);
        fs.SetLength(0);
      }
      catch (Exception ex)
      {
        ShowError?.Invoke($"Cannot clear log: {ex.Message}");
        return;
      }
      Refresh();
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke(this, EventArgs.Empty);

    // Mirrors Settings.ctor's writer selection: configured folder wins
    // if WriteLog is on and the folder actually exists, otherwise the
    // hard-coded fallback path App.xaml.cs writes to before Settings
    // has had a chance to swap loggers.
    private string ResolveLogFolder()
    {
      if (_settings._writeLog &&
          !string.IsNullOrEmpty(_settings._writeLogPath) &&
          Directory.Exists(_settings._writeLogPath))
        return _settings._writeLogPath;

      var fallback = Path.Combine(Path.GetTempPath(), "PictureSlideshow");
      return Directory.Exists(fallback) ? fallback : null;
    }

    private static string FindMostRecentLog(string folder)
    {
      if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        return null;

      var files = Directory.GetFiles(folder, "*.txt");
      if (files.Length == 0)
        return null;

      return files
        .Select(f => new FileInfo(f))
        .OrderByDescending(fi => fi.LastWriteTimeUtc)
        .First()
        .FullName;
    }

    private static string FormatStatus(long totalBytes, int shownChars)
    {
      string total = FormatBytes(totalBytes);
      return totalBytes > TailMaxBytes
        ? $"showing last {FormatBytes(shownChars)} of {total}"
        : $"showing {total}";
    }

    private static string FormatBytes(long n)
    {
      if (n < 1024) return $"{n} B";
      if (n < 1024 * 1024) return $"{n / 1024.0:F1} KB";
      return $"{n / (1024.0 * 1024.0):F1} MB";
    }
  }
}
