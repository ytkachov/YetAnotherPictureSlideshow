using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using PictureSlideshowScreensaver.Models;
using Serilog;
using Yaps.Core.Abstractions;
using Yaps.Core.Models.Stats;

namespace PictureSlideshowScreensaver.ViewModels
{
  /// <summary>
  /// View model for the S-key show-registry viewer. Renders the same text
  /// the flush service writes daily, built from the live in-memory counters,
  /// so pressing S always shows the current state rather than the last file
  /// on disk. Save both persists the registry and drops a timestamped copy
  /// of the report next to it.
  /// </summary>
  public partial class StatsViewerViewModel : ObservableObject
  {
    private readonly IPhotoStatistics _stats;
    private readonly Settings _settings;
    private readonly PhotoStatisticsOptions _options;

    [ObservableProperty] private string _reportText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _statsPath = "";

    // Wired by the view — kept off the VM so it stays testable without a Window.
    public Action<string> ShowError { get; set; }
    public event EventHandler RequestClose;

    public StatsViewerViewModel(IPhotoStatistics stats, Settings settings, IOptions<PhotoStatisticsOptions> options)
    {
      _stats = stats;
      _settings = settings;
      _options = options.Value;
      _statsPath = string.IsNullOrEmpty(_options.StatsFilePath) ? "(registry not persisted)" : _options.StatsFilePath;
    }

    [RelayCommand]
    private void Refresh()
    {
      try
      {
        var report = _stats.BuildReport();
        ReportText = PhotoStatsReportFormatter.ToText(report);
        StatusText = $"фото: {report.LibraryPhotoCount}, показов: {report.TotalShows}, " +
                     $"ни разу: {report.NeverShown}, не читаются: {report.FailedPhotoCount}";
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Building the photo stats report failed");
        ReportText = $"(не удалось построить отчёт: {ex.Message})";
        StatusText = "";
      }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
      try
      {
        // Persist the counters first: the whole point of a manual save is to
        // make the current numbers durable, not just to dump the text.
        await _stats.FlushAsync().ConfigureAwait(true);

        var folder = ResolveFolder();
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"pss_stat_{DateTime.Now:yyyy-MM-dd-HHmm}.txt");
        await File.WriteAllTextAsync(path, ReportText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
                  .ConfigureAwait(true);

        StatusText = $"сохранено: {path}";
        Log.Information("Photo stats report saved on demand to {Path}", path);
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Saving the photo stats report failed");
        ShowError?.Invoke($"Не удалось сохранить отчёт: {ex.Message}");
      }
    }

    [RelayCommand]
    private void OpenFolder()
    {
      var folder = ResolveFolder();
      if (!Directory.Exists(folder))
      {
        ShowError?.Invoke("Папка со статистикой не существует.");
        return;
      }

      try
      {
        // UseShellExecute is required for the explorer.exe association on .NET 8.
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
      }
      catch (Exception ex)
      {
        ShowError?.Invoke($"Не удалось открыть папку: {ex.Message}");
      }
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke(this, EventArgs.Empty);

    private string ResolveFolder()
    {
      var configured = Path.GetDirectoryName(_options.StatsFilePath);
      return string.IsNullOrEmpty(configured) ? _settings.ResolveStatsFolder() : configured;
    }
  }
}
