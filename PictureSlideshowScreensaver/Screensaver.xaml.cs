using System;
using System.Windows;
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
  }

}
