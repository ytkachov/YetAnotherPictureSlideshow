using System;
using System.IO;
using Microsoft.Win32.TaskScheduler;
using Serilog;

namespace WeatherCollector
{
  /// <summary>
  /// Registers / refreshes the Task Scheduler entry that runs
  /// WeatherCollector.exe every 15 minutes. Previously this logic was
  /// woven into WeatherFileReaderWriter (legacy Weather/ library); SRP
  /// puts the scheduling concern next to the executable it schedules.
  /// </summary>
  public static class TaskSchedulerInstaller
  {
    private const string SchedulerFolder = "YetAnotherPictureSlideshow";
    private const string TaskName = "WeatherCollector";

    public static void Ensure(string execArgs)
    {
      string execFolder = AppContext.BaseDirectory;
      string execPath = Path.Combine(execFolder, "WeatherCollector.exe");

      try
      {
        using var ts = new TaskService();
        var fullName = SchedulerFolder + "\\" + TaskName;
        var existing = ts.GetTask(fullName);

        bool recreate = false;
        if (existing != null)
        {
          foreach (var action in existing.Definition.Actions)
          {
            if (action is not ExecAction ea) continue;
            bool sameTarget = string.Equals(ea.Path, execPath, StringComparison.OrdinalIgnoreCase) &&
                              string.Equals(ea.WorkingDirectory, execFolder, StringComparison.OrdinalIgnoreCase);
            bool sameArgs = string.Equals(ea.Arguments ?? string.Empty, execArgs ?? string.Empty, StringComparison.Ordinal);
            if (sameTarget && sameArgs) continue;

            var folder = ts.RootFolder.FindFolder(SchedulerFolder);
            folder?.DeleteTask(TaskName, false);
            recreate = true;
            break;
          }
        }

        if (existing == null || recreate)
        {
          var td = ts.NewTask();
          td.RegistrationInfo.Description = "Read weather info from web and store it into file";
          td.Principal.LogonType = TaskLogonType.InteractiveToken;
          td.Settings.Enabled = true;
          td.Settings.ExecutionTimeLimit = TimeSpan.FromMinutes(5);
          td.Settings.Hidden = false;

          td.Actions.Add(new ExecAction(execPath, execArgs, execFolder));

          var trigger = (DailyTrigger)td.Triggers.Add(new DailyTrigger());
          trigger.StartBoundary = DateTime.Now + TimeSpan.FromSeconds(10);
          trigger.RandomDelay = TimeSpan.FromSeconds(60);
          trigger.EndBoundary = DateTime.MaxValue;
          trigger.ExecutionTimeLimit = TimeSpan.FromSeconds(90);
          trigger.Repetition.Duration = TimeSpan.FromHours(24);
          trigger.Repetition.Interval = TimeSpan.FromMinutes(15);

          var folder = ts.RootFolder.FindFolder(SchedulerFolder) ?? ts.RootFolder.CreateFolder(SchedulerFolder, null, false);
          folder.RegisterTaskDefinition(TaskName, td);
        }
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "Could not register Task Scheduler entry");
      }
    }

  }

  internal static class TaskFolderExtensions
  {
    public static TaskFolder FindFolder(this TaskFolder self, string foldername)
    {
      foreach (var tf in self.SubFolders)
      {
        if (string.Equals(tf.Name, foldername, StringComparison.OrdinalIgnoreCase))
          return tf;
      }
      return null;
    }
  }
}
