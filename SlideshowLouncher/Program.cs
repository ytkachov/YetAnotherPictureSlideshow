using System.Diagnostics;

namespace SlideshowLouncher
{
    internal class Program
    {
        private const string TargetProcess = "PictureSlideshowScreensaver";
        private const string TargetExe = "PictureSlideshowScreensaver.exe";

        static int Main(string[] args)
        {
            // Don't relaunch if the screensaver is already up, or if a
            // developer has Visual Studio open (devenv) — debugging would
            // race against the autorestart.
            var processes = Process.GetProcesses();
            foreach (var p in processes)
            {
                if (p.ProcessName.StartsWith(TargetProcess, StringComparison.OrdinalIgnoreCase))
                    return 0;
                if (p.ProcessName.StartsWith("devenv", StringComparison.OrdinalIgnoreCase))
                    return 0;
            }

            // Resolve the screensaver next to the launcher first, then
            // fall back to the Release directory of the dev tree. The
            // previous hardcoded absolute path was unusable on any other
            // machine.
            var exe = LocateScreensaver(args);
            if (exe == null)
            {
                Fail($"Could not find {TargetExe}. Pass its path as the first argument, " +
                     "or place the launcher next to the screensaver executable.");
                return 1;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
                });
                Log($"started {exe}");
                return 0;
            }
            catch (Exception ex)
            {
                Fail($"Failed to start {exe}: {ex.Message}");
                return 1;
            }
        }

        private static string? LocateScreensaver(string[] args)
        {
            if (args.Length > 0 && File.Exists(args[0]))
                return args[0];

            var nextToLauncher = Path.Combine(AppContext.BaseDirectory, TargetExe);
            if (File.Exists(nextToLauncher))
                return nextToLauncher;

            // Dev-tree fallback: walk up from bin\<Config>\<Tfm>\ looking for a
            // sibling PictureSlideshowScreensaver output. Stripping a fixed
            // number of segments was off by one — AppContext.BaseDirectory ends
            // with a separator, so the first GetDirectoryName removed only that
            // separator and the search landed inside the launcher's own folder.
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                foreach (var config in new[] { "Release", "Debug" })
                {
                    var candidate = Path.Combine(
                        dir.FullName,
                        "PictureSlideshowScreensaver",
                        "bin", config, "net8.0-windows",
                        TargetExe);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            return null;
        }

        private static void Fail(string message)
        {
            Console.Error.WriteLine(message);
            Log(message);
        }

        // This is a WinExe, so Task Scheduler gives it no console and
        // Console.Error goes nowhere — a launcher that cannot find the
        // screensaver would fail silently forever. Mirror the screensaver's
        // fallback log folder, but as .log: the in-app L-key viewer tails the
        // newest *.txt there and must keep showing the Serilog file.
        private static void Log(string message)
        {
            try
            {
                var folder = Path.Combine(Path.GetTempPath(), "PictureSlideshow");
                Directory.CreateDirectory(folder);
                File.AppendAllText(
                    Path.Combine(folder, "launcher.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch
            {
                // Best effort — a launcher that can't write its log must still launch.
            }
        }
    }
}
