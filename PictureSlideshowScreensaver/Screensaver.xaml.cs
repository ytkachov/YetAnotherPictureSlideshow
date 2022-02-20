using System.Windows;
using System.Windows.Input;

namespace PictureSlideshowScreensaver
{

  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class Screensaver : Window
  {
    public Screensaver(object datacontext)
    {
      DataContext = datacontext;  
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
