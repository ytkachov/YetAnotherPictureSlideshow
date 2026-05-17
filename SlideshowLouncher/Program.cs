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
                Console.Error.WriteLine(
                    $"Could not find {TargetExe}. Pass its path as the first argument, " +
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
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to start {exe}: {ex.Message}");
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

            // Dev-tree fallback: ..\PictureSlideshowScreensaver\bin\<config>\net8.0-windows\
            var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(AppContext.BaseDirectory)));
            if (root != null)
            {
                foreach (var config in new[] { "Release", "Debug" })
                {
                    var candidate = Path.Combine(
                        Path.GetDirectoryName(root) ?? "",
                        "PictureSlideshowScreensaver",
                        "bin", config, "net8.0-windows",
                        TargetExe);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            return null;
        }
    }
}
