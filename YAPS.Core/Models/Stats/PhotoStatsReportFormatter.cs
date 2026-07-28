using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Yaps.Core.Models.Stats;

/// <summary>
/// Renders a <see cref="PhotoStatsReport"/> as fixed-width text. One
/// formatter for both consumers — the daily file written by the flush
/// service and the S-key viewer inside the slideshow — so what the user
/// reads on screen is exactly what lands in the file.
/// </summary>
public static class PhotoStatsReportFormatter
{
    // Show counts above this collapse into a single "N+" row so a photo that
    // ran away with 300 shows doesn't produce 300 histogram lines.
    private const int HistogramTailFrom = 20;

    private const int MaxFolderRows = 200;
    private const int MaxFailureRows = 100;

    public static string ToText(PhotoStatsReport r)
    {
        var sb = new StringBuilder(16 * 1024);
        var ci = CultureInfo.InvariantCulture;

        sb.AppendLine("=== Реестр показов фотографий ===");
        sb.AppendLine($"Отчёт создан:            {Local(r.GeneratedUtc)}");
        if (r.SinceUtc is DateTime since)
        {
            int days = Math.Max(1, (int)Math.Round((r.GeneratedUtc - since).TotalDays));
            sb.AppendLine($"Реестр ведётся с:        {Local(since)} ({days} дн.)");
        }
        else
        {
            sb.AppendLine("Реестр ведётся с:        (показов ещё не было)");
        }
        sb.AppendLine();

        sb.AppendLine("Библиотека");
        if (!r.LibraryKnown)
            sb.AppendLine("  (скан библиотеки ещё не завершён — цифры по накопленному реестру)");
        sb.AppendLine($"  Фотографий в библиотеке:      {r.LibraryPhotoCount,10}");
        sb.AppendLine($"  Записей в реестре:            {r.TrackedPhotoCount,10}");
        if (r.MissingFromLibraryCount > 0)
            sb.AppendLine($"  Нет в библиотеке (удалены?):  {r.MissingFromLibraryCount,10}");
        sb.AppendLine($"  Показывались хотя бы раз:     {r.ShownAtLeastOnce,10}  {Percent(r.ShownAtLeastOnce, r.LibraryPhotoCount)}");
        sb.AppendLine($"  Ни разу не показаны:          {r.NeverShown,10}  {Percent(r.NeverShown, r.LibraryPhotoCount)}");
        sb.AppendLine($"  Не удалось прочитать:         {r.FailedPhotoCount,10}");
        sb.AppendLine();

        sb.AppendLine("Показы");
        sb.AppendLine($"  Всего показов:                {r.TotalShows,10}");
        sb.AppendLine($"  В среднем на фото:            {r.AverageShows.ToString("F2", ci),10}");
        sb.AppendLine($"  Медиана:                      {r.MedianShows,10}");
        sb.AppendLine($"  Максимум:                     {r.MaxShows,10}");
        sb.AppendLine($"  Коэффициент Джини:            {r.GiniCoefficient.ToString("F3", ci),10}   (0 = все фото поровну, 1 = всё достаётся одному)");
        sb.AppendLine();

        AppendHistogram(sb, r.Histogram);
        AppendMostShown(sb, r);
        AppendFailures(sb, r);
        AppendFolders(sb, r, ci);

        return sb.ToString();
    }

    private static void AppendHistogram(StringBuilder sb, IReadOnlyList<ShowCountBucket> histogram)
    {
        sb.AppendLine("Распределение (сколько фотографий показано ровно N раз)");
        if (histogram.Count == 0)
        {
            sb.AppendLine("  (нет данных)");
            sb.AppendLine();
            return;
        }

        int tailPhotos = 0;
        foreach (var bucket in histogram)
        {
            if (bucket.ShowCount >= HistogramTailFrom)
            {
                tailPhotos += bucket.Photos;
                continue;
            }
            sb.AppendLine($"  {bucket.ShowCount,5} раз : {bucket.Photos,8} фото");
        }
        if (tailPhotos > 0)
            sb.AppendLine($"  {HistogramTailFrom,4}+ раз : {tailPhotos,8} фото");
        sb.AppendLine();
    }

    private static void AppendMostShown(StringBuilder sb, PhotoStatsReport r)
    {
        if (r.MostShown.Count == 0)
            return;

        sb.AppendLine($"Чаще всего показывались (топ-{r.MostShown.Count})");
        foreach (var e in r.MostShown)
            sb.AppendLine($"  {e.ShowCount,5}  {Local(e.LastShownUtc),16}  {e.Path}");
        sb.AppendLine();
    }

    private static void AppendFailures(StringBuilder sb, PhotoStatsReport r)
    {
        sb.AppendLine($"Не удалось прочитать ({r.Failures.Count})");
        if (r.Failures.Count == 0)
        {
            sb.AppendLine("  (таких нет)");
            sb.AppendLine();
            return;
        }

        foreach (var e in r.Failures.Take(MaxFailureRows))
        {
            sb.AppendLine($"  {e.FailureCount,3} ош.  {Local(e.LastFailureUtc),16}  {e.Path}");
            if (!string.IsNullOrEmpty(e.LastError))
                sb.AppendLine($"          {e.LastError}");
        }
        if (r.Failures.Count > MaxFailureRows)
            sb.AppendLine($"  … и ещё {r.Failures.Count - MaxFailureRows}");
        sb.AppendLine();
    }

    private static void AppendFolders(StringBuilder sb, PhotoStatsReport r, CultureInfo ci)
    {
        sb.AppendLine("По папкам (сортировка по среднему числу показов на фото)");
        sb.AppendLine("   сред.     фото   показов   ни разу  папка");
        foreach (var f in r.Folders.Take(MaxFolderRows))
        {
            sb.AppendLine($"  {f.AverageShows.ToString("F2", ci),6}  {f.PhotoCount,7}  {f.TotalShows,8}  {f.NeverShown,8}  {f.Folder}");
        }
        if (r.Folders.Count > MaxFolderRows)
            sb.AppendLine($"  … и ещё {r.Folders.Count - MaxFolderRows} папок");
        sb.AppendLine();
    }

    private static string Percent(int part, int total)
        => total == 0 ? "" : $"({(100.0 * part / total).ToString("F1", CultureInfo.InvariantCulture)}%)";

    private static string Local(DateTime? utc)
        => utc is DateTime d
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "—";
}
