using System;
using System.Collections.Generic;
using System.Linq;

namespace Yaps.Core.Models.Stats;

/// <summary>How many photos have been shown exactly <see cref="ShowCount"/> times.</summary>
public sealed record ShowCountBucket(int ShowCount, int Photos);

/// <summary>
/// Per-folder aggregate. This is the row that answers "are the photos evenly
/// distributed" in practice: the slideshow picks a random FOLDER and then N
/// random photos out of it, so a folder of 5 photos hands each of them far
/// more screen time than a folder of 3000.
/// </summary>
public sealed record FolderShowStats(
    string Folder,
    int PhotoCount,
    int NeverShown,
    long TotalShows,
    double AverageShows,
    int FailedPhotos);

/// <summary>
/// Aggregated view over the show registry. Built by <see cref="Build"/> from
/// the raw entries — pure computation, no I/O, so it is equally usable from
/// the flush service (daily text report) and the on-demand viewer.
/// </summary>
public sealed record PhotoStatsReport(
    DateTime GeneratedUtc,
    DateTime? SinceUtc,
    bool LibraryKnown,
    int LibraryPhotoCount,
    int TrackedPhotoCount,
    int MissingFromLibraryCount,
    int ShownAtLeastOnce,
    int NeverShown,
    int FailedPhotoCount,
    long TotalShows,
    double AverageShows,
    int MedianShows,
    int MaxShows,
    double GiniCoefficient,
    IReadOnlyList<ShowCountBucket> Histogram,
    IReadOnlyList<PhotoStatEntry> MostShown,
    IReadOnlyList<PhotoStatEntry> Failures,
    IReadOnlyList<FolderShowStats> Folders)
{
    public const int DefaultTopCount = 25;

    // Bound on how many failing photos travel in the report. A library-wide
    // storage failure could otherwise put tens of thousands of entries into a
    // string the UI has to render.
    private const int MaxFailures = 500;

    public static PhotoStatsReport Build(IEnumerable<PhotoStatEntry> entries, DateTime generatedUtc, int topCount = DefaultTopCount)
    {
        var all = entries as IReadOnlyList<PhotoStatEntry> ?? entries.ToList();

        // Photos the current scan found are the population the distribution
        // stats describe. Before the first scan completes (or when the library
        // is unreachable) fall back to everything we have on record so the
        // viewer still says something useful.
        var population = all.Where(e => e.InLibrary).ToList();
        bool libraryKnown = population.Count > 0;
        if (!libraryKnown)
            population = all.ToList();

        int missing = all.Count - population.Count;
        long totalShows = population.Sum(e => (long)e.ShowCount);
        int shownAtLeastOnce = population.Count(e => e.ShowCount > 0);
        int failed = population.Count(e => e.FailureCount > 0);

        var counts = population.Select(e => e.ShowCount).OrderBy(c => c).ToArray();

        var histogram = population
            .GroupBy(e => e.ShowCount)
            .OrderBy(g => g.Key)
            .Select(g => new ShowCountBucket(g.Key, g.Count()))
            .ToList();

        var mostShown = population
            .Where(e => e.ShowCount > 0)
            .OrderByDescending(e => e.ShowCount)
            .ThenByDescending(e => e.LastShownUtc ?? DateTime.MinValue)
            .Take(topCount)
            .Select(e => e.Copy())
            .ToList();

        // Failures are reported across everything on record, not just the
        // current library: a photo that vanished after failing is exactly the
        // case worth seeing.
        var failures = all
            .Where(e => e.FailureCount > 0)
            .OrderByDescending(e => e.FailureCount)
            .ThenByDescending(e => e.LastFailureUtc ?? DateTime.MinValue)
            .Take(MaxFailures)
            .Select(e => e.Copy())
            .ToList();

        var folders = population
            .GroupBy(e => GetFolder(e.Path), StringComparer.OrdinalIgnoreCase)
            .Select(g => new FolderShowStats(
                g.Key,
                g.Count(),
                g.Count(e => e.ShowCount == 0),
                g.Sum(e => (long)e.ShowCount),
                g.Average(e => (double)e.ShowCount),
                g.Count(e => e.FailureCount > 0)))
            .OrderByDescending(f => f.AverageShows)
            .ThenByDescending(f => f.PhotoCount)
            .ToList();

        DateTime? since = null;
        foreach (var e in all)
        {
            if (e.FirstShownUtc is DateTime first && (since is null || first < since))
                since = first;
        }

        return new PhotoStatsReport(
            GeneratedUtc: generatedUtc,
            SinceUtc: since,
            LibraryKnown: libraryKnown,
            LibraryPhotoCount: population.Count,
            TrackedPhotoCount: all.Count,
            MissingFromLibraryCount: libraryKnown ? missing : 0,
            ShownAtLeastOnce: shownAtLeastOnce,
            NeverShown: population.Count - shownAtLeastOnce,
            FailedPhotoCount: failed,
            TotalShows: totalShows,
            AverageShows: population.Count == 0 ? 0 : (double)totalShows / population.Count,
            MedianShows: Median(counts),
            MaxShows: counts.Length == 0 ? 0 : counts[^1],
            GiniCoefficient: Gini(counts),
            Histogram: histogram,
            MostShown: mostShown,
            Failures: failures,
            Folders: folders);
    }

    private static string GetFolder(string path)
    {
        int i = path.LastIndexOfAny(['\\', '/']);
        return i <= 0 ? path : path[..i];
    }

    private static int Median(int[] sorted)
    {
        if (sorted.Length == 0)
            return 0;
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>
    /// Gini coefficient over the show counts: 0 = every photo got the same
    /// amount of screen time, 1 = one photo got all of it. One number that
    /// answers "is the rotation fair" without reading the whole histogram.
    /// Input must be sorted ascending.
    /// </summary>
    private static double Gini(int[] sorted)
    {
        if (sorted.Length == 0)
            return 0;

        long sum = 0;
        double weighted = 0;
        for (int i = 0; i < sorted.Length; i++)
        {
            sum += sorted[i];
            weighted += (double)(i + 1) * sorted[i];
        }

        if (sum == 0)
            return 0;

        int n = sorted.Length;
        return (2.0 * weighted) / (n * (double)sum) - (n + 1.0) / n;
    }
}
