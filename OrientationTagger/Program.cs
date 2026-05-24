using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Yaps.Core.Abstractions;
using Yaps.Core.Models;
using Yaps.Infrastructure;
using Yaps.Infrastructure.Orientation;
using Yaps.Infrastructure.Settings;

class Program
{
    // Actionable EXIF codes — the model reliably distinguishes 90 CW vs 90 CCW
    // (codes 6 and 8), but its 180 (code 3) calls are essentially always wrong
    // on this library (validated 2026-05). So code 3 is recorded as Attempted
    // but never written to Orientation; the photo stays unrotated.
    private static readonly HashSet<int> ActionableCodes = new() { 6, 8 };
    private const double MinConfidence = 0.5;

    static List<string> SafeGetImages(string folderPath)
    {
        var result = new List<string>();
        SafeGetImagesRecursive(folderPath, result);
        return result;
    }

    // Borrowed wholesale from GeoTagger: enumerate files and directories
    // separately so a single bad entry never aborts the scan.
    static void SafeGetImagesRecursive(string folderPath, List<string> result)
    {
        var files = TryEnumerate(() => Directory.EnumerateFiles(folderPath), folderPath, "files");
        if (files != null)
        {
            using var e = files.GetEnumerator();
            while (true)
            {
                string f;
                try
                {
                    if (!e.MoveNext()) break;
                    f = e.Current;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Skipping bad file entry in: {Path}", folderPath);
                    break;
                }
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".jpg" || ext == ".jpeg")
                    result.Add(f);
            }
        }

        var dirs = TryEnumerate(() => Directory.EnumerateDirectories(folderPath), folderPath, "directories");
        if (dirs != null)
        {
            using var e = dirs.GetEnumerator();
            while (true)
            {
                string d;
                try
                {
                    if (!e.MoveNext()) break;
                    d = e.Current;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Skipping bad directory entry in: {Path}", folderPath);
                    break;
                }
                SafeGetImagesRecursive(d, result);
            }
        }
    }

    static IEnumerable<string> TryEnumerate(Func<IEnumerable<string>> get, string folderPath, string kind)
    {
        try { return get(); }
        catch (Exception ex)
        {
            Log.Warning(ex, "Cannot enumerate {Kind} in: {Path}", kind, folderPath);
            return null;
        }
    }

    static int Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .CreateLogger();

        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: OrientationTagger <path-to-photos-folder>");
            return 1;
        }

        string folderPath = args[0];

        if (!Directory.Exists(folderPath))
        {
            Log.Error("Folder not found: {Folder}", folderPath);
            return 1;
        }

        // Same composition pattern as GeoTagger: AddInfrastructure picks up the
        // FinfoStoreOptions from the Registry so sidecars go to the configured
        // FinfoFolder (matches what the screensaver reads).
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddInfrastructure(RegistryConfig.ReadFinfoStoreOptions());

        // The ONNX model ships next to the executable as recognition/orientation.onnx
        // (linked from tools/orientation/ at build time).
        var modelPath = Path.Combine(AppContext.BaseDirectory, "recognition", "orientation.onnx");
        builder.Services.AddSingleton<IOrientationDetector>(_ => new OnnxOrientationDetector(modelPath));

        using var host = builder.Build();
        host.Start();

        var detector = host.Services.GetRequiredService<IOrientationDetector>();
        var finfoStore = host.Services.GetRequiredService<IFinfoStore>();

        try
        {
            return Run(folderPath, detector, finfoStore);
        }
        finally
        {
            host.StopAsync().GetAwaiter().GetResult();
            if (detector is IDisposable d) d.Dispose();
        }
    }

    static int Run(string folderPath, IOrientationDetector detector, IFinfoStore finfoStore)
    {
        var imageFiles = SafeGetImages(folderPath).ToArray();
        Log.Information("Found {Count} JPEG files in {Folder} (including subfolders)", imageFiles.Length, folderPath);

        int processed = 0;
        int written = 0;       // wrote Orientation (rotation applied)
        int markedNoop = 0;    // attempted, no actionable rotation (upright / low conf / 180)
        int skippedSet = 0;    // already had Orientation
        int skippedAttempted = 0; // already had OrientationDetectionAttempted
        int errors = 0;

        foreach (var imagePath in imageFiles)
        {
            processed++;
            if (processed % 100 == 0)
                Log.Information("Progress: {Processed}/{Total} | written: {Written} | noop: {Noop} | skipped (set): {SkipSet} | skipped (attempted): {SkipAtt} | errors: {Errors}",
                    processed, imageFiles.Length, written, markedNoop, skippedSet, skippedAttempted, errors);

            FinfoData existing;
            try
            {
                existing = finfoStore.Read(imagePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to read .finfo for {Image}", imagePath);
                errors++;
                continue;
            }

            if (existing != null && existing.Orientation is int set && set >= 1 && set <= 8)
            {
                skippedSet++;
                continue;
            }
            if (existing != null && existing.OrientationDetectionAttempted)
            {
                skippedAttempted++;
                continue;
            }

            OrientationDetection result;
            try
            {
                using var bmp = new Bitmap(imagePath);
                result = detector.Detect(bmp);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Detection failed for {Image} -- skipping (not marked)", imagePath);
                errors++;
                continue;
            }

            var data = existing ?? new FinfoData();
            data.OrientationDetectionAttempted = true;

            bool actionable = ActionableCodes.Contains(result.Code) && result.Confidence >= MinConfidence;
            if (actionable)
            {
                data.Orientation = result.Code;
                // Faces (if any cached) were detected against the un-rotated
                // bitmap and would land in the wrong place once the screensaver
                // rotates. Drop them; FindFaces will recompute on next display.
                data.Faces = null;
                written++;
                Log.Information("[{Processed}/{Total}] {Image}: code {Code} (conf {Conf:F3})",
                    processed, imageFiles.Length, Path.GetFileName(imagePath), result.Code, result.Confidence);
            }
            else
            {
                markedNoop++;
            }

            try
            {
                finfoStore.Write(imagePath, data);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to write .finfo for {Image}", imagePath);
                errors++;
            }
        }

        Log.Information("Done. Written: {Written}, Noop (attempted): {Noop}, Skipped (set): {SkipSet}, Skipped (attempted): {SkipAtt}, Errors: {Errors}",
            written, markedNoop, skippedSet, skippedAttempted, errors);
        return errors > 0 ? 1 : 0;
    }
}
