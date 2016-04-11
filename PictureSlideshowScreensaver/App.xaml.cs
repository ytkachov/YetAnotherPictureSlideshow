using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace PictureSlideshowScreensaver
{
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App : Application
  {
    private void Application_Startup(object sender, StartupEventArgs e)
    {
      if (e.Args.Length > 0)
      {
        string first = e.Args[0].ToLower().Trim();
        string second = null;

        if (first.Length > 2)
        {
          second = first.Substring(3).Trim();
          first = first.Substring(0, 2);
        }
        else if (e.Args.Length > 1)
        {
          second = e.Args[1];
        }


        // Configuration mode
        if (first == "/c")
        {
          new Configuration().Show();
        }

        // Preview mode
        else if (first == "/p")
        {
          // No Preview mode implemented!
          Application.Current.Shutdown();
        }

        // Full-screen mode
        else if (first == "/s")
        {
          LaunchScreensaver();
        }

        // Undefined argument
        else
        {
          //MessageBox.Show("Unknown argument");
          Application.Current.Shutdown();
        }
      }
      else
      {
        // No argument, launch screensaver.
        //MessageBox.Show("No argument");
        LaunchScreensaver();
      }
    }

    private void LaunchScreensaver()
    {

      RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\PictureSlideshowScreensaver");
      int interval = int.Parse((string)key.GetValue("Interval"));
      System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
      int offsetStep = interval / screens.Length;
      int offset = 0;

      for (int i = 0; i < screens.Length; i++)
      {
        System.Windows.Forms.Screen s = screens[i];
        Screensaver scr = new Screensaver(s.Bounds, offset);
        offset += offsetStep;

        scr.WindowStartupLocation = WindowStartupLocation.Manual;
        scr.Left = s.Bounds.X;
        scr.Top = s.Bounds.Y;
        scr.Width = s.Bounds.Width;
        scr.Height = s.Bounds.Height;

        scr.Show();
      }

      foreach (System.Windows.Forms.Screen screen in System.Windows.Forms.Screen.AllScreens)
      {
        //#if !DEBUG
        //                if (screen.Bounds.X > 0) {
        //#endif


        //#if !DEBUG
        //                }
        //#endif
      }
    }
  }
}
