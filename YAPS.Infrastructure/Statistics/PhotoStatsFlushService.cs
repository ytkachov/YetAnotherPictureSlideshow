using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Yaps.Core.Abstractions;
using Yaps.Core.Models.Stats;

namespace Yaps.Infrastructure.Statistics;

/// <summary>
/// Writes the show registry out on a slow cadence (default every 6 h) and
/// once more on clean shutdown. Counters are kept in RAM between flushes on
/// purpose — a photo frame writes to eMMC / SD / a network share, and one
/// small file a few times a day is the whole storage cost of the feature.
///
/// When a report folder is configured it also drops a human-readable
/// <c>pss_stat_yyyy-MM-dd.txt</c> next to it — same text the S-key viewer
/// shows, one file per day (rewritten in place, so it doesn't pile up).
/// </summary>
public sealed class PhotoStatsFlushService : BackgroundService
{
    // The final flush must not be aborted mid-write by the host's shutdown
    // budget, but it must not hang the appliance either.
    private static readonly TimeSpan ShutdownFlushBudget = TimeSpan.FromSeconds(4);

    private readonly IPhotoStatistics _stats;
    private readonly PhotoStatisticsOptions _options;

    public PhotoStatsFlushService(IPhotoStatistics stats, IOptions<PhotoStatisticsOptions> options)
    {
        _stats = stats;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let Host.StartAsync return promptly; nothing here is needed for the
        // slideshow to come up.
        await Task.Yield();

        var interval = _options.FlushInterval > TimeSpan.Zero ? _options.FlushInterval : TimeSpan.FromHours(6);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await FlushOnceAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // CancellationToken.None on purpose: the host's stop budget cancelling
        // half-way through would just throw away everything accumulated since
        // the last flush. The write itself is temp-file + atomic move, so even
        // an abandoned wait can't corrupt the existing registry.
        try
        {
            await FlushOnceAsync(CancellationToken.None).WaitAsync(ShutdownFlushBudget).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Log.Warning("Photo stats flush did not finish within {Budget}; shutting down anyway", ShutdownFlushBudget);
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task FlushOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _stats.FlushAsync(cancellationToken).ConfigureAwait(false);
            await WriteReportAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down between the two writes — nothing to report.
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Photo stats flush failed");
        }
    }

    private async Task WriteReportAsync(CancellationToken cancellationToken)
    {
        var folder = _options.ReportFolder;
        if (string.IsNullOrWhiteSpace(folder))
            return;

        try
        {
            Directory.CreateDirectory(folder);
            var report = _stats.BuildReport();
            var text = PhotoStatsReportFormatter.ToText(report);
            var path = Path.Combine(folder, $"pss_stat_{DateTime.Now:yyyy-MM-dd}.txt");

            // BOM so Notepad on the appliance renders the Cyrillic labels
            // instead of guessing the ANSI codepage.
            await File.WriteAllTextAsync(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken)
                      .ConfigureAwait(false);
            Log.Information("Photo stats report written to {Path}", path);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not write photo stats report to {Folder}", folder);
        }
    }
}
