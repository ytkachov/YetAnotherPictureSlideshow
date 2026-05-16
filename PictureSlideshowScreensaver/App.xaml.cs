using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PictureSlideshowScreensaver.Composition;
using Serilog;

namespace PictureSlideshowScreensaver
{
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App : Application
  {
    private IHost _host;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
      // Build the DI host up front so command-line modes (/c, /s) can pull
      // the window and view model out of the container rather than newing
      // them up by hand. _host.Start() is non-blocking; OnExit takes care
      // of orderly shutdown.
      _host = Host.CreateApplicationBuilder()
          .ConfigureServices()
          .Build();
      _host.Start();

      if (e.Args.Length > 0)
      {
        string first = e.Args[0].ToLower().Trim();

        if (first.Length > 2)
          first = first.Substring(0, 2);

        // Configuration mode
        if (first == "/c")
        {
          new Configuration().Show();
        }
        // Preview mode — not implemented; exit cleanly.
        else if (first == "/p")
        {
          Application.Current.Shutdown();
        }
        // Full-screen mode
        else if (first == "/s")
        {
          LaunchScreensaver();
        }
        else
        {
          Application.Current.Shutdown();
        }
      }
      else
      {
        LaunchScreensaver();
      }
    }

    protected override void OnExit(ExitEventArgs e)
    {
      try
      {
        // Synchronous wait is acceptable here because the dispatcher is
        // already shutting down. StopAsync gives IHostedService instances
        // (none right now, but future ones) a chance to drain.
        _host?.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "IHost.StopAsync threw");
      }
      finally
      {
        _host?.Dispose();
        _host = null;
        Log.CloseAndFlush();
      }

      base.OnExit(e);
    }

    private void LaunchScreensaver()
    {
      var screens = System.Windows.Forms.Screen.AllScreens;
      if (screens.Length == 0)
        return;

      var s = screens[0];
      var scr = _host.Services.GetRequiredService<Screensaver>();

      scr.WindowStartupLocation = WindowStartupLocation.Manual;
      scr.Left = s.Bounds.X;
      scr.Top = s.Bounds.Y;
      scr.Width = s.Bounds.Width;
      scr.Height = s.Bounds.Height;

      scr.Show();
    }
  }

  internal static class HostBuilderExtensions
  {
    public static HostApplicationBuilder ConfigureServices(this HostApplicationBuilder builder)
    {
      builder.Services.AddScreensaver();
      return builder;
    }
  }
}
