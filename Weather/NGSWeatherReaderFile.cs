using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace weather
{

  public class NGSFileReader : INGSWeatherReader
  {
    private string _foldername = ".";
    private static string _filename = "ngs_weather_info.txt";
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

    public void navigate(string url = null)
    {
    }

    public void restart()
    {
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
      string info = readfile();
      string[] separators = { delimiter };

      return info.Split(separators, StringSplitOptions.RemoveEmptyEntries);
    }
  }

}
