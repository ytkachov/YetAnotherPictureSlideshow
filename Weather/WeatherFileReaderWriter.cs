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

  public interface IWeatherWriter
  {
    void writeinfo(string temperature, string current, string forecast, string except);
  }

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

  public abstract class WeatherFileReaderWriter : IWeatherReader, IWeatherWriter
  {
    protected string _execparams = ". 1";
    protected string _foldername = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
    protected string _filename = "weather_info.txt";
    protected bool _checkcollector = true;

    static private string delimiter = "\n###$$$%%%@@@***&&&\n";

    protected virtual string get_execparams()
    {
      return _execparams;
    }

    protected virtual string get_filename()
    {
      return _filename;
    }

    protected virtual string get_foldername()
    {
      return _foldername;
    }

    public WeatherFileReaderWriter(string folder = null)
    {
      if (folder != null)
        _foldername = folder;
      else
        _foldername = get_foldername();

      _execparams = get_execparams();
      _filename = get_filename();
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
          StreamWriter sw = new StreamWriter(fs);

          sw.Write(temperature);
          sw.Write(delimiter);
          sw.Write(current);
          sw.Write(delimiter);
          sw.Flush();
          sw.Write(forecast);
          sw.Flush();
          sw.Write(delimiter);
          sw.Flush();
          sw.Write(except);
          sw.Flush();

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

            if (eaction.Path == execname && eaction.WorkingDirectory == execfolder)
            {
              if (eaction.Arguments == _execparams)
                continue;
              if (string.IsNullOrEmpty(eaction.Arguments) && string.IsNullOrEmpty(_execparams))
                continue;
            }

            try
            {
              //t.Definition.Actions.RemoveAt(0);

              TaskFolder tf = ts.RootFolder.FindFolder(schedulerfolder);
              tf.DeleteTask(schedulertaskname);
              recreate = true;
              break;
            }
            catch (Exception e)
            {
              string em = e.Message;
            }
          }
        }
        
        if (t == null || recreate)
        {
          // Create a new task definition and assign properties
          TaskDefinition td = ts.NewTask();
          td.RegistrationInfo.Description = "Read weather info from web and store it into file";
          td.Principal.LogonType = TaskLogonType.InteractiveToken;
          td.Settings.Enabled = true;
          td.Settings.ExecutionTimeLimit = TimeSpan.FromMinutes(5);
          td.Settings.Hidden = false;

          td.Actions.Add(new ExecAction(execname, _execparams, execfolder));

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

  public class NGSFileReaderWriter : WeatherFileReaderWriter
  {

    public NGSFileReaderWriter(WeatherSource type, string folder = null) : base(folder)
    {
      _foldername = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
      _filename = "ngs_weather_info.txt";
      _execparams = $". {(int)type}";
    }
  }

  public class YandexFileReaderWriter : WeatherFileReaderWriter
  {

    public YandexFileReaderWriter(WeatherSource type, string folder = null) : base(folder)
    {
      _foldername = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
      _filename = "yandex_weather_info.txt";
      _execparams = $". {(int)type}";
    }

  }

}
