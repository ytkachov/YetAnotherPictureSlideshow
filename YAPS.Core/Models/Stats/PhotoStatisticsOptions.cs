using System;

namespace Yaps.Core.Models.Stats;

/// <summary>
/// Runtime knobs for the show registry. Populated at composition time from
/// the Registry-backed <c>Settings</c>.
/// </summary>
public sealed class PhotoStatisticsOptions
{
    /// <summary>
    /// Where the registry itself (JSON) lives. Empty disables persistence —
    /// counters still work for the session, they just don't survive a restart.
    /// </summary>
    public string StatsFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Folder for the human-readable report written on every flush
    /// (<c>pss_stat_yyyy-MM-dd.txt</c>, one file per day, rewritten in place).
    /// Null / empty = don't write it.
    /// </summary>
    public string? ReportFolder { get; set; }

    /// <summary>
    /// How often the in-memory counters are written out. Deliberately coarse:
    /// the point of keeping the registry in RAM is not to write to the
    /// appliance's storage on every slide. A flush also happens on clean
    /// shutdown, so this interval only bounds what a power cut can cost.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromHours(6);
}
