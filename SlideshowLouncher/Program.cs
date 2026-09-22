using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace SlideshowLouncher
{
    internal class Program
    {
        private const string TargetProcess = "PictureSlideshowScreensaver";
        private const string TargetExe = "PictureSlideshowScreensaver.exe";
        private const string TaskName = @"YetAnotherPictureSlideshow\SlideshowLauncher";
        private const int DefaultIntervalMinutes = 5;

        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal))
                return RunVerb(args);

            return Launch(args);
        }

        private static int Launch(string[] args)
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

        // --- Task Scheduler registration -------------------------------------

        private static int RunVerb(string[] args)
        {
            // WinExe has no console of its own; borrow the caller's so the
            // install verbs can actually answer the person who typed them.
            AttachConsole(AttachParentProcess);

            return args[0] switch
            {
                "--install" => Install(args),
                "--uninstall" => Uninstall(),
                "--help" => Usage(null),
                _ => Usage($"Unrecognised verb '{args[0]}'.")
            };
        }

        private static int Install(string[] args)
        {
            var everyMinutes = DefaultIntervalMinutes;
            string? screensaver = null;

            for (var i = 1; i < args.Length; i++)
            {
                if (args[i] == "--every" && i + 1 < args.Length)
                {
                    if (!int.TryParse(args[++i], out var minutes))
                        return Usage($"--every expects a number of minutes, got '{args[i]}'.");
                    everyMinutes = Math.Clamp(minutes, 1, 1440);
                }
                else if (args[i] == "--screensaver" && i + 1 < args.Length)
                {
                    screensaver = Path.GetFullPath(args[++i]);
                    if (!File.Exists(screensaver))
                        return Usage($"No such file: {screensaver}");
                }
                else
                {
                    return Usage($"Unrecognised option '{args[i]}'.");
                }
            }

            var launcher = Environment.ProcessPath;
            if (launcher == null)
                return Report(1, "Could not determine the launcher's own path.");

            // Bake the screensaver path into the action when it is visible now:
            // the frame's task then survives the launcher being moved later.
            screensaver ??= LocateScreensaver([]);
            if (screensaver == null)
            {
                Console.WriteLine(
                    $"Warning: {TargetExe} not found next to the launcher - registering without an " +
                    "explicit path. Pass --screensaver <path> if the task logs 'Could not find'.");
            }

            var xmlPath = Path.Combine(Path.GetTempPath(), $"slideshow-task-{Environment.ProcessId}.xml");
            try
            {
                // schtasks /xml only accepts UTF-16.
                File.WriteAllText(xmlPath, BuildTaskXml(launcher, screensaver, everyMinutes), Encoding.Unicode);

                var exitCode = RunSchTasks($"/create /tn \"{TaskName}\" /xml \"{xmlPath}\" /f");
                if (exitCode != 0)
                {
                    return Report(exitCode,
                        $"schtasks refused the task (exit code {exitCode}). Re-run it by hand to see why: " +
                        $"schtasks /create /tn \"{TaskName}\" /xml \"{xmlPath}\" /f");
                }

                return Report(0,
                    $"Registered '{TaskName}': runs {Path.GetFileName(launcher)} at logon and every " +
                    $"{everyMinutes} min as {Environment.UserDomainName}\\{Environment.UserName}" +
                    (screensaver == null ? "." : $", starting {screensaver}."));
            }
            catch (Exception ex)
            {
                return Report(1, $"Failed to register '{TaskName}': {ex.Message}");
            }
            finally
            {
                try { File.Delete(xmlPath); } catch { /* temp file — best effort */ }
            }
        }

        private static int Uninstall()
        {
            // Query first: schtasks reports "not found" in the console's own
            // language, so exit codes are the only locale-proof signal.
            if (RunSchTasks($"/query /tn \"{TaskName}\"") != 0)
                return Report(0, $"No '{TaskName}' task registered - nothing to remove.");

            var exitCode = RunSchTasks($"/delete /tn \"{TaskName}\" /f");
            return exitCode == 0
                ? Report(0, $"Removed '{TaskName}'.")
                : Report(exitCode, $"schtasks failed with exit code {exitCode} while deleting '{TaskName}'.");
        }

        private static string BuildTaskXml(string launcher, string? screensaver, int everyMinutes)
        {
            var user = SecurityElement.Escape($"{Environment.UserDomainName}\\{Environment.UserName}");
            var command = SecurityElement.Escape(launcher);
            var workingDirectory = SecurityElement.Escape(Path.GetDirectoryName(launcher) ?? "");
            var arguments = screensaver == null
                ? ""
                : $"{Environment.NewLine}      <Arguments>\"{SecurityElement.Escape(screensaver)}\"</Arguments>";

            // The first timed run is one interval out, so installing this does
            // not blank the desktop the moment the command returns; the logon
            // trigger covers a cold start of the frame.
            var start = DateTime.Now.AddMinutes(everyMinutes).ToString("yyyy-MM-ddTHH:mm:ss");

            return $"""
                <?xml version="1.0" encoding="UTF-16"?>
                <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                  <RegistrationInfo>
                    <Description>Starts the YetAnotherPictureSlideshow screensaver unless it is already running.</Description>
                  </RegistrationInfo>
                  <Triggers>
                    <LogonTrigger>
                      <Enabled>true</Enabled>
                      <UserId>{user}</UserId>
                    </LogonTrigger>
                    <CalendarTrigger>
                      <StartBoundary>{start}</StartBoundary>
                      <Enabled>true</Enabled>
                      <ScheduleByDay>
                        <DaysInterval>1</DaysInterval>
                      </ScheduleByDay>
                      <Repetition>
                        <Interval>PT{everyMinutes}M</Interval>
                        <StopAtDurationEnd>false</StopAtDurationEnd>
                      </Repetition>
                    </CalendarTrigger>
                  </Triggers>
                  <Principals>
                    <Principal id="Author">
                      <UserId>{user}</UserId>
                      <LogonType>InteractiveToken</LogonType>
                      <RunLevel>LeastPrivilege</RunLevel>
                    </Principal>
                  </Principals>
                  <Settings>
                    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                    <AllowHardTerminate>true</AllowHardTerminate>
                    <StartWhenAvailable>true</StartWhenAvailable>
                    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                    <IdleSettings>
                      <StopOnIdleEnd>false</StopOnIdleEnd>
                      <RestartOnIdle>false</RestartOnIdle>
                    </IdleSettings>
                    <AllowStartOnDemand>true</AllowStartOnDemand>
                    <Enabled>true</Enabled>
                    <Hidden>false</Hidden>
                    <RunOnlyIfIdle>false</RunOnlyIfIdle>
                    <WakeToRun>false</WakeToRun>
                    <ExecutionTimeLimit>PT1H</ExecutionTimeLimit>
                    <Priority>7</Priority>
                  </Settings>
                  <Actions Context="Author">
                    <Exec>
                      <Command>{command}</Command>{arguments}
                      <WorkingDirectory>{workingDirectory}</WorkingDirectory>
                    </Exec>
                  </Actions>
                </Task>
                """;
        }

        private static int RunSchTasks(string arguments)
        {
            using var schtasks = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                // Swallow schtasks' own chatter: it speaks the console's OEM
                // codepage, which decodes to mojibake on a localised Windows.
                // Exit codes carry the outcome; our own messages carry the rest.
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (schtasks == null)
                return 1;

            schtasks.WaitForExit();
            return schtasks.ExitCode;
        }

        private static int Usage(string? error)
        {
            if (error != null)
                Console.Error.WriteLine(error);

            Console.WriteLine($"""
                SlideshowLouncher - starts {TargetExe} unless it is already running.

                  SlideshowLouncher.exe [<path to {TargetExe}>]
                  SlideshowLouncher.exe --install [--every <minutes>] [--screensaver <path>]
                  SlideshowLouncher.exe --uninstall

                --install registers the scheduled task '{TaskName}' for the
                current user: at logon, then every {DefaultIntervalMinutes} minutes by default. It runs with
                the interactive token - a task started in session 0 never reaches the desktop.
                """);

            return error == null ? 0 : 1;
        }

        private static int Report(int exitCode, string message)
        {
            if (exitCode == 0)
                Console.WriteLine(message);
            else
                Console.Error.WriteLine(message);

            Log(message);
            return exitCode;
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

        private const int AttachParentProcess = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachConsole(int processId);
    }
}
