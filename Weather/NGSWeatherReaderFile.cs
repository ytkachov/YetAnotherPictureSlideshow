using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.TaskScheduler;
using TS = Microsoft.Win32.TaskScheduler;

namespace weather
{

  static class TSExtensions
  {
    //public static ElementCollection Children(this Element self)
    //{
    //  return self.DomContainer.Elements.Filter(e => self.Equals(e.Parent));
    //}
    public static TaskFolder FindFolder(this TaskFolder self, string foldername)
    {
      foreach (var tf in self.SubFolders)
      {
        if (tf.Name.Equals(foldername, StringComparison.OrdinalIgnoreCase))
          return tf;
      }

      return null;
    }
  }

  public class NGSFileReader : INGSWeatherReader
  {
    private string _foldername = ".";
    private string _filename = "ngs_weather_info.txt";
    private bool _checkcollector = true;

    static private string delimiter = "\n###$$$%%%@@@***&&&\n";

    public NGSFileReader(string folder = null)
    {
      if (folder != null)
        _foldername = folder;
    }

    public void close()
    {
    }

    public string temperature()
    {
      string[] parts = splitinfo();

      if (parts.Length > 0)
        return parts[0];

      return "";
    }

    public string current()
    {
      string[] parts = splitinfo();

      if (parts.Length > 1)
        return parts[1];

      return "";
    }

    public string forecast()
    {
      string[] parts = splitinfo();

      if (parts.Length > 2)
        return parts[2];

      return "";
    }

    public void getrest()
    {
    }

    public void restart()
    {
      _checkcollector = true;
    }

    public void writeinfo(string temperature, string current, string forecast, string except)
    {
      string info = temperature + delimiter + current + delimiter + forecast;
      string fname = Path.Combine(_foldername, _filename);

      for (int count = 0; count < 5; count++)
      {
        try
        {
          FileStream fs = new FileStream(fname, FileMode.Create, FileAccess.Write, FileShare.None);
          StreamWriter sr = new StreamWriter(fs);

          sr.Write(temperature);
          sr.Write(delimiter);
          sr.Write(current);
          sr.Write(delimiter);
          sr.Flush();
          sr.Write(forecast);
          sr.Flush();
          sr.Write(delimiter);
          sr.Flush();
          sr.Write(except);
          sr.Flush();

          fs.Close();

          break;
        }
        catch
        {

        }
        Thread.Sleep(500);
      }
    }

    private string readfile()
    {
      string fname = Path.Combine(_foldername, _filename);
      string info = "";


      if (File.Exists(fname))
      {
        DateTime wt = File.GetLastWriteTime(fname);
        TimeSpan fileage = DateTime.Now - wt;

        if (fileage.TotalMinutes < 30.0)
        {
          for (int count = 0; count < 5; count++)
          {
            try
            {
              FileStream fs = new FileStream(fname, FileMode.Open, FileAccess.Read, FileShare.None);
              StreamReader sr = new StreamReader(fs);

              info = sr.ReadToEnd();
              fs.Close();

              break;
            }
            catch
            {

            }
            Thread.Sleep(500);
          }
        }
      }

      if (info.Length == 0)
        _checkcollector = true;

      return info;
    }

    private string [] splitinfo()
    {
      string info = readfile();

      if (_checkcollector)
        checkcollectorparams();

      string[] separators = { delimiter };
      return info.Split(separators, StringSplitOptions.RemoveEmptyEntries);
    }

    private void checkcollectorparams()
    {
      using (TaskService ts = new TaskService())
      {
        string execparams = "";
        string execfolder = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
        string execname = Path.Combine(execfolder, "WeatherCollector.exe");

        string schedulerfolder = "YetAnotherPictureSlideshow";
        string schedulertaskname = "WeatherCollector";
        string taskname = schedulerfolder + @"\" + schedulertaskname;

        bool recreate = false;
        TS.Task t = ts.GetTask(taskname);
        if (t != null)
        {
          // check if existing task correspnds to current parameters
          foreach (var action in t.Definition.Actions)
          {
            var eaction = action as ExecAction;
            if (eaction == null)
              continue;

            if (eaction.Arguments == execparams && eaction.Path == execname && eaction.WorkingDirectory == execfolder)
              continue;

            TaskFolder tf = ts.RootFolder.FindFolder(schedulerfolder);
            tf.DeleteTask(schedulertaskname);
            recreate = true;
            break;
          }
        }
        
        if (t == null || recreate)
        {
          // Create a new task definition and assign properties
          TaskDefinition td = ts.NewTask();
          td.RegistrationInfo.Description = "Read weather info from pogoda.ngs.ru and store it into file";
          td.Principal.LogonType = TaskLogonType.InteractiveToken;
          td.Settings.Enabled = true;
          td.Settings.ExecutionTimeLimit = TimeSpan.FromMinutes(5);
          td.Settings.Hidden = false;

          td.Actions.Add(new ExecAction(execname, execparams, execfolder));

          // Add a trigger that will fire the task at this time every other day
          DailyTrigger dt = (DailyTrigger)td.Triggers.Add(new DailyTrigger());
          dt.StartBoundary = DateTime.Now + TimeSpan.FromSeconds(10);
          dt.RandomDelay = TimeSpan.FromSeconds(60);
          dt.EndBoundary = DateTime.MaxValue;
          dt.ExecutionTimeLimit = TimeSpan.FromSeconds(90);
          dt.Repetition.Duration = TimeSpan.FromHours(24);
          dt.Repetition.Interval = TimeSpan.FromMinutes(15);

          // Register the task in the folder
          TaskFolder tf = ts.RootFolder.FindFolder(schedulerfolder);
          if (tf == null)
            tf = ts.RootFolder.CreateFolder(schedulerfolder, null, false);

          tf.RegisterTaskDefinition(schedulertaskname, td);
        }

        _checkcollector = false;
      }
    }
  }
}
