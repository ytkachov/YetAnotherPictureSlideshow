using System;
using System.Windows;
using System.Windows.Input;
using PictureSlideshowScreensaver.ViewModels;

namespace PictureSlideshowScreensaver
{

  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class Screensaver : Window
  {
    public Screensaver(ScreensaverViewModel viewModel)
    {
      DataContext = viewModel;
      InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
      // Maximize window
      //this.WindowState = System.Windows.WindowState.Maximized;
#if DEBUG
      this.Topmost = false;
      this.WindowStyle = WindowStyle.ThreeDBorderWindow;
      this.ResizeMode = ResizeMode.CanResizeWithGrip;
#endif
    }

    protected override void OnClosed(EventArgs e)
    {
      base.OnClosed(e);
      // Tear down the VM (and any DispatcherTimers / event subscriptions
      // it owns) so we don't lean on the process exit to clean up. In
      // debug builds the window can be closed without shutting down WPF.
      (DataContext as IDisposable)?.Dispose();
    }

    private void Shutdown()
    {
      Application.Current.Shutdown();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.Key == Key.Escape)
        Shutdown();
      else if (e.Key == Key.F)
      {
        // show weather forecast
        WeatherForecast.Visibility = (WeatherForecast.Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible);
      }
    }
  }

}
