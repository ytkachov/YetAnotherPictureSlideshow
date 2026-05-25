using System.Windows.Controls;

namespace presenters
{
  /// <summary>
  /// Interaction logic for WeatherForecast.xaml. DataContext is
  /// <c>ForecastViewModel</c>, set in <c>Screensaver.xaml</c>; each
  /// tile binds its informer through the VM. Stage 6.4 deleted the
  /// hidden-probe timer that synced column widths — Grid SharedSizeGroup
  /// inside Weather.xaml does that work now.
  /// </summary>
  public partial class WeatherForecast : UserControl
  {
    public WeatherForecast()
    {
      InitializeComponent();
    }
  }
}
