using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Yaps.Core.Models.Stats;

namespace Yaps.Core.Abstractions;

/// <summary>
/// Long-running registry of how often each photo has actually been put on
/// screen, plus which photos failed to load. Counters live in memory and are
/// persisted on a slow cadence (see <see cref="FlushAsync"/>) so an appliance
/// running off an SD card / eMMC isn't written to on every slide.
///
/// Implementations must be safe to call from several threads: the slideshow
/// picks photos on the UI thread, prefetches them on a worker, and the library
/// scan runs on its own task.
/// </summary>
public interface IPhotoStatistics
{
    /// <summary>
    /// Declares the set of photos the current library scan found. Photos not
    /// seen before are added with a zero count (so "never shown" is a fact the
    /// report can state, not an absence of data), and entries whose file is no
    /// longer in the library are flagged as missing rather than dropped —
    /// history is worth keeping across a re-organised photo tree.
    /// </summary>
    void RegisterLibrary(IReadOnlyCollection<string> imagePaths);

    /// <summary>Counts one selection of <paramref name="imagePath"/> for display.</summary>
    void RecordShown(string imagePath);

    /// <summary>
    /// Records that the photo could not be loaded / decoded. The reason is a
    /// short human-readable message (usually <c>Exception.Message</c>).
    /// </summary>
    void RecordFailure(string imagePath, string reason);

    /// <summary>
    /// How often the photo has been shown across the whole recorded history,
    /// 0 for one the registry has never seen. Used by the rotation to put
    /// under-shown photos at the front of the queue after a restart.
    /// </summary>
    int GetShowCount(string imagePath);

    /// <summary>Aggregates the current in-memory registry into a report.</summary>
    PhotoStatsReport BuildReport();

    /// <summary>
    /// Persists the registry if anything changed since the last write. Cheap
    /// no-op when clean, so callers can flush liberally (timer tick, shutdown).
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
