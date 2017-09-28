using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Threading;

namespace presenters
{
  /// <summary>
  /// Interaction logic for WeatherForecast.xaml
  /// </summary>
  public partial class WeatherForecast : UserControl
  {
    private DispatcherTimer _checkComponentWidth;

    static weather.WeatherPeriod[] _weather_periods = (weather.WeatherPeriod[])Enum.GetValues(typeof(weather.WeatherPeriod));
    Weather[] _components;

    int[] _components_width = { 0, 0, 0, 0, 0 };
    int[] _prev_components_width = { 0, 0, 0, 0, 0 };
    private int _current_weather_period = 0;

    public WeatherForecast()
    {
      InitializeComponent();

      _components = new Weather [] 
      {
        (Weather)W_TodayNight, (Weather)W_TodayMorning, (Weather)W_TodayDay, (Weather)W_TodayEvening,
        (Weather)W_TomorrowNight, (Weather)W_TomorrowMorning, (Weather)W_TomorrowDay, (Weather)W_TomorrowEvening,
        (Weather)W_AfterTomorrowNight, (Weather)W_AfterTomorrowMorning, (Weather)W_AfterTomorrowDay, (Weather)W_AfterTomorrowEvening
      };

      _checkComponentWidth = new DispatcherTimer();
      _checkComponentWidth.Interval = TimeSpan.FromSeconds(1);
      _checkComponentWidth.Tick += new EventHandler(checkWidthTick);
      _checkComponentWidth.Start();

      W_Invisible.LayoutUpdated += W_Invisible_LayoutUpdated;
    }

    private void W_Invisible_LayoutUpdated(object sender, EventArgs e)
    {
      if (W_Invisible.WeatherPeriod == weather.WeatherPeriod.Undefined)
        return;

      if (W_Invisible.ComponentWidths == null)
        return;

      string [] cw = W_Invisible.ComponentWidths.Split(',');
      for (int i = 0; i < _components_width.Length; i++)
        if (i >= cw.Length)
          break;
        else
        {
          double dw = 0.0;
          if (double.TryParse(cw[i], out dw))
          {
            int idw = (int)(dw + 0.5);
            _components_width[i] = _components_width[i] < idw ? idw : _components_width[i];
          }
        }
    }

    void checkWidthTick(object sender, EventArgs e)
    {
      if (++_current_weather_period < _weather_periods.Length)
      {
        _checkComponentWidth.Interval = TimeSpan.FromMilliseconds(100);
        W_Invisible.WeatherPeriod = _weather_periods[_current_weather_period];
      }
      else
      {

        if (!Enumerable.SequenceEqual(_prev_components_width, _components_width))
        {
          _prev_components_width = _components_width;

          string cwidth = "";
          for (int i = 0; i < _components_width.Length; i++)
            cwidth += (cwidth.Length == 0 ? "" : ",") + _components_width[i].ToString();

          foreach (Weather w in _components)
            w.ChildrenWidths = cwidth;
        }

        _checkComponentWidth.Interval = TimeSpan.FromSeconds(10);
        _current_weather_period = 0;
        _components_width = new int [] { 0, 0, 0, 0, 0 };
        W_Invisible.WeatherPeriod = _weather_periods[_current_weather_period];

      }
    }

    private void OnLayoutUpdated(object sender, EventArgs e)
    {
      Weather [] components = 
      {
        (Weather)W_TodayNight, (Weather)W_TodayMorning, (Weather)W_TodayDay, (Weather)W_TodayEvening,
        (Weather)W_TomorrowNight, (Weather)W_TomorrowMorning, (Weather)W_TomorrowDay, (Weather)W_TomorrowEvening,
        (Weather)W_AfterTomorrowNight, (Weather)W_AfterTomorrowMorning, (Weather)W_AfterTomorrowDay, (Weather)W_AfterTomorrowEvening
      };

      double[] mw = { 0, 0, 0, 0, 0 };
      foreach(Weather w in components)
      {
        var cw = w.ComponentWidths.Split(',');
        for (int i = 0; i < mw.Length; i++)
          if (i >= cw.Length)
            break;
          else
          {
            double dw = 0.0;
            if (double.TryParse(cw[i], out dw))
            mw[i] = mw[i] < dw ? dw : mw[i];
          }
      }
    }
  
  }
}
