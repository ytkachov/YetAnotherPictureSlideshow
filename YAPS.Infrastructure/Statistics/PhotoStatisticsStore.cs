using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Yaps.Core.Abstractions;
using Yaps.Core.Models.Stats;

namespace Yaps.Infrastructure.Statistics;

/// <summary>
/// In-memory show registry with a lazily loaded JSON backing file.
///
/// Everything is guarded by one lock. Contention is a non-issue — the hot
/// path is one increment per displayed photo (seconds apart) — and a single
/// lock keeps the counters, the dirty flag and the report snapshot mutually
/// consistent without an interlocked-increment puzzle over mutable entries.
///
/// The file is written by <see cref="FlushAsync"/> only; the write happens
/// outside the lock (snapshot first) so a slow / unreachable disk can't stall
/// the slideshow, and it lands via a temp file + atomic move so a power cut
/// mid-write can't truncate the accumulated history.
/// </summary>
public sealed class PhotoStatisticsStore : IPhotoStatistics
{
    private const int CurrentSchemaVersion = 1;

    // Enough to identify the failure; keeps a pathological exception message
    // from bloating every entry in the file.
    private const int MaxErrorLength = 300;

    private readonly PhotoStatisticsOptions _options;
    private readonly object _sync = new();
    private readonly Dictionary<string, PhotoStatEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private bool _loaded;
    private bool _dirty;

    public PhotoStatisticsStore(PhotoStatisticsOptions options)
    {
        _options = options;
    }

    public void RegisterLibrary(IReadOnlyCollection<string> imagePaths)
    {
        lock (_sync)
        {
            EnsureLoaded();

            // Recomputed from scratch on every scan: a photo that disappeared
            // from the library keeps its history but stops counting towards
            // the distribution stats.
            foreach (var entry in _entries.Values)
                entry.InLibrary = false;

            foreach (var path in imagePaths)
            {
                if (string.IsNullOrEmpty(path))
                    continue;

                GetOrAdd(path).InLibrary = true;
            }
        }
    }

    public void RecordShown(string imagePath)
    {
        if (string.IsNullOrEmpty(imagePath))
            return;

        var now = DateTime.UtcNow;
        lock (_sync)
        {
            EnsureLoaded();
            var entry = GetOrAdd(imagePath);
            entry.ShowCount++;
            entry.FirstShownUtc ??= now;
            entry.LastShownUtc = now;
            _dirty = true;
        }
    }

    public void RecordFailure(string imagePath, string reason)
    {
        if (string.IsNullOrEmpty(imagePath))
            return;

        var now = DateTime.UtcNow;
        lock (_sync)
        {
            EnsureLoaded();
            var entry = GetOrAdd(imagePath);
            entry.FailureCount++;
            entry.LastFailureUtc = now;
            entry.LastError = Truncate(reason);
            _dirty = true;
        }
    }

    public int GetShowCount(string imagePath)
    {
        if (string.IsNullOrEmpty(imagePath))
            return 0;

        lock (_sync)
        {
            EnsureLoaded();
            return _entries.TryGetValue(imagePath, out var entry) ? entry.ShowCount : 0;
        }
    }

    public PhotoStatsReport BuildReport()
    {
        lock (_sync)
        {
            EnsureLoaded();
            // Copies, not the live entries: the report is formatted outside
            // the lock and must not observe a half-applied increment.
            var snapshot = _entries.Values.Select(e => e.Copy()).ToList();
            return PhotoStatsReport.Build(snapshot, DateTime.UtcNow);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        string path = _options.StatsFilePath;
        if (string.IsNullOrEmpty(path))
            return;

        PhotoStatsFile file;
        lock (_sync)
        {
            if (!_dirty)
                return;

            _dirty = false;
            file = new PhotoStatsFile
            {
                SchemaVersion = CurrentSchemaVersion,
                SavedUtc = DateTime.UtcNow,
                Entries = _entries.Values
                    .Where(e => e.ShowCount > 0 || e.FailureCount > 0)
                    .Select(e => e.Copy())
                    .ToList(),
            };
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteAsync(path, file, cancellationToken).ConfigureAwait(false);
            Log.Information("Photo stats registry saved: {Entries} entries -> {Path}", file.Entries.Count, path);
        }
        catch (Exception ex)
        {
            // Put the dirty flag back so the next flush retries instead of
            // silently dropping everything accumulated since the last write.
            lock (_sync)
                _dirty = true;

            Log.Warning(ex, "Could not save photo stats registry to {Path}", path);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static async Task WriteAsync(string path, PhotoStatsFile file, CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        var temp = path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
                                                 bufferSize: 64 * 1024, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, file, PhotoStatsJsonContext.Default.PhotoStatsFile, cancellationToken)
                                .ConfigureAwait(false);
        }

        File.Move(temp, path, overwrite: true);
    }

    // Caller holds _sync.
    private PhotoStatEntry GetOrAdd(string path)
    {
        if (!_entries.TryGetValue(path, out var entry))
        {
            entry = new PhotoStatEntry { Path = path };
            _entries[path] = entry;
        }
        return entry;
    }

    // Caller holds _sync. _loaded is set before the read so a corrupt /
    // unreadable file costs one attempt, not one per photo.
    private void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;

        string path = _options.StatsFilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        try
        {
            var json = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize(json, PhotoStatsJsonContext.Default.PhotoStatsFile);
            if (file?.Entries is null)
                return;

            foreach (var entry in file.Entries)
            {
                if (string.IsNullOrEmpty(entry.Path))
                    continue;

                entry.InLibrary = false;
                _entries[entry.Path] = entry;
            }

            Log.Information("Photo stats registry loaded: {Entries} entries from {Path} (saved {Saved:u})",
                _entries.Count, path, file.SavedUtc);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not read photo stats registry {Path}; starting from empty", path);
        }
    }

    private static string Truncate(string reason)
    {
        if (string.IsNullOrEmpty(reason))
            return string.Empty;

        reason = reason.Replace('\r', ' ').Replace('\n', ' ');
        return reason.Length <= MaxErrorLength ? reason : reason[..MaxErrorLength];
    }
}
