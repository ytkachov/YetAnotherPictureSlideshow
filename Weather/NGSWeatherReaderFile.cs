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

    public void writeinfo(string temperature, string current, string forecast)
    {
      string info = temperature + delimiter + current + delimiter + forecast;
      string fname = Path.Combine(_foldername, _filename);

      for (int count = 0; count < 5; count++)
      {
        try
        {
          FileStream fs = new FileStream(fname, FileMode.Create, FileAccess.Write, FileShare.None);
          StreamWriter sr = new StreamWriter(fs);

          sr.Write(info);
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

      return info;
    }

    private string [] splitinfo()
    {
      if (_checkcollector)
        checkcollectorparams();

      string info = readfile();
      string[] separators = { delimiter };

      return info.Split(separators, StringSplitOptions.RemoveEmptyEntries);
    }

    private void checkcollectorparams()
    {
      _checkcollector = false;

      using (TaskService ts = new TaskService())
      {
        string execname, execparams, execfolder;
        string schedulerfolder = "YetAnotherPictureSlideshow";
        string schedulertaskname = "WeatherCollector";
        string taskname = schedulerfolder + @"\" + schedulertaskname;


        TS.Task t = ts.GetTask(taskname);
        if (t == null)
        {
          // Create a new task definition and assign properties
          TaskDefinition td = ts.NewTask();
          td.RegistrationInfo.Description = "Read weather info from pogoda.ngs.ru and store it into file";
          td.Principal.LogonType = TaskLogonType.InteractiveToken;
          td.Settings.Enabled = true;
          td.Settings.ExecutionTimeLimit = TimeSpan.FromHours(2);
          td.Settings.Hidden = false;

          td.Actions.Add(new ExecAction(execname, execparams, execfolder));

          // Add a trigger that will fire the task at this time every other day
          TimeTrigger tt = (TimeTrigger)td.Triggers.Add(new TimeTrigger());
          tt.StartBoundary = DateTime.Now + TimeSpan.FromSeconds(10);
          tt.EndBoundary = DateTime.Today + TimeSpan.MaxValue;
          tt.ExecutionTimeLimit = TimeSpan.FromSeconds(90);
          // tt.Repetition.Duration = TimeSpan.FromMinutes(20);
          tt.Repetition.Interval = TimeSpan.FromMinutes(15);

          // Register the task in the root folder
          TaskFolder tf = ts.RootFolder.CreateFolder(schedulerfolder, ts.RootFolder.GetAccessControl());
          tf.RegisterTaskDefinition(schedulertaskname, td);
        }

      }
    }
  }

}
