using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using PictureSlideshowScreensaver.ViewModels;

class DateFormatter
{
  public static   string[] weekdays = { "Воскресенье", "Понедельник", "Вторник", "Среда", "Четверг", "Пятница", "Суббота" };
  internal static string[] weekdays_short = { "Вс", "Пн", "Вт", "Ср", "Чт", "Пт", "Сб" };
  internal static string[] monthes = { "Января", "Февраля", "Марта", "Апреля", "Мая", "Июня", "Июля", "Августа", "Сентября", "Октября", "Ноября", "Декабря" };
  internal static string[] monthes_short = { "Янв", "Фев", "Мар", "Апр", "Май", "Июн", "Июл", "Авг", "Сен", "Окт", "Ноя", "Дек" };
}

namespace informers
{

  public class PhotoProperties : BaseViewModel
  {
    private string _faces_found = "";
    private string _date_taken = "21/12/1997";
    private string _rotation_glyph = "";

    public string PhotoDescription { get { return _date_taken; } set { _date_taken = value; RaisePropertyChanged(); } }
    public string FacesFound { get { return _faces_found; } }
    public void SetFacesFound(int num)
    {
        _faces_found = "";
        for (int i = 0; i < num; i++)
          _faces_found += "\u263B";

        RaisePropertyChanged("FacesFound");
    }

    /// <summary>
    /// Single-glyph hint shown to the LEFT of <see cref="FacesFound"/> when the
    /// current photo's stored pixels are rotated relative to display upright.
    /// Empty string = no rotation (or a pure flip, which we deliberately skip);
    /// the XAML cell collapses via StringToVisibilityConverter.
    /// </summary>
    public string RotationGlyph { get { return _rotation_glyph; } }

    public void SetRotation(System.Drawing.RotateFlipType rf)
    {
      _rotation_glyph = rf switch
      {
        System.Drawing.RotateFlipType.Rotate90FlipNone  => "\u21BB",          // \u21BB  EXIF 6 (viewer rotates 90 CW)
        System.Drawing.RotateFlipType.Rotate270FlipNone => "\u21BA",          // \u21BA  EXIF 8 (viewer rotates 90 CCW)
        System.Drawing.RotateFlipType.Rotate180FlipNone => "\u21BB\u21BB",    // \u21BB\u21BB EXIF 3 (180)
        _ => "",                                                              // Normal / flips: no indicator
      };
      RaisePropertyChanged(nameof(RotationGlyph));
    }
  }

  class DBGInformer : INotifyPropertyChanged
  {
    private string _weather_provider_msg = "msg";


    public string Weather_Provider_Msg { get { return _weather_provider_msg; } set { _weather_provider_msg = value; RaisePropertyChanged("Weather_Provider_Msg"); } }

    public event PropertyChangedEventHandler PropertyChanged;
    private void RaisePropertyChanged(string propertyName)
    {
      if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
    }
  }

  class DateTimeInformer : BaseViewModel
  {
    private int    _time_Tick = 0;
    private string _time_Hours = "00";
    private string _time_Minutes = "00";
    private string _time_Seconds = "00";
    private string _date_Full = "Пт 01 Янв 2016";
    private string _date_DayMon = "01/05";
    private string _date_DayMonTomorrow = "02/05";
    private string _date_DayMonAfterTomorrow = "03/05";
    private int    _lastMinute = -1;
    private DispatcherTimer _clockTick = new DispatcherTimer();

    public DateTimeInformer()
    {
      _clockTick.Tick += new EventHandler(clock_Tick);
      _clockTick.Interval = TimeSpan.FromSeconds(1.0);
      _clockTick.Start();

      // The Informer is instantiated from XAML, so we have no caller that
      // could call Dispose explicitly. Hook the dispatcher shutdown to
      // stop the timer and unhook the event handler before the process
      // tears down — otherwise the closure would keep this VM alive
      // until the AppDomain unloads.
      Dispatcher.CurrentDispatcher.ShutdownStarted += OnDispatcherShutdown;
    }

    private void OnDispatcherShutdown(object sender, EventArgs e)
    {
      _clockTick.Stop();
      _clockTick.Tick -= clock_Tick;
      Dispatcher.CurrentDispatcher.ShutdownStarted -= OnDispatcherShutdown;
    }

    public string Time_Hours { get { return _time_Hours; } set { if (_time_Hours != value) { _time_Hours = value; RaisePropertyChanged(); } } }
    public string Time_Minutes { get { return _time_Minutes; } set { if (_time_Minutes != value) { _time_Minutes = value; RaisePropertyChanged(); } } }
    public string Time_Seconds { get { return _time_Seconds; } set { if (_time_Seconds != value) { _time_Seconds = value; RaisePropertyChanged(); } } }
    public string Date_Full { get { return _date_Full; } set { if (_date_Full != value) { _date_Full = value; RaisePropertyChanged(); } } }
    public string Date_DayMon { get { return _date_DayMon; } set { if (_date_DayMon != value) { _date_DayMon = value; RaisePropertyChanged(); } } }
    public string Date_DayMonTomorrow { get { return _date_DayMonTomorrow; } set { if (_date_DayMonTomorrow != value) { _date_DayMonTomorrow = value; RaisePropertyChanged(); } } }
    public string Date_DayMonAfterTomorrow { get { return _date_DayMonAfterTomorrow; } set { if (_date_DayMonAfterTomorrow != value) { _date_DayMonAfterTomorrow = value; RaisePropertyChanged(); } } }
    public int Time_Tick { get { return _time_Tick; } set { if (_time_Tick != value) { _time_Tick = value; RaisePropertyChanged(); } } }

    void clock_Tick(object sender, EventArgs e)
    {
      var now = DateTime.Now;

      // Seconds and the colon-blink toggle move every tick.
      Time_Tick = 1 - Time_Tick;
      Time_Seconds = now.Second.ToString("D2");

      // Hour, minute and the three date captions only change on a minute
      // boundary; recomputing the four date strings every second (12+
      // array indexes + concatenations) was wasted work on every tick.
      if (now.Minute == _lastMinute)
        return;
      _lastMinute = now.Minute;

      Time_Hours = now.Hour.ToString("D2");
      Time_Minutes = now.Minute.ToString("D2");

      var tomorrow = now.AddDays(1);
      var afterTomorrow = now.AddDays(2);
      Date_Full = DateFormatter.weekdays_short[(int)now.DayOfWeek] + ", " + now.Day.ToString() + " " + DateFormatter.monthes_short[now.Month - 1];
      Date_DayMon = Date_Full;
      Date_DayMonTomorrow = DateFormatter.weekdays_short[(int)tomorrow.DayOfWeek] + ", " + tomorrow.Day.ToString() + " " + DateFormatter.monthes_short[tomorrow.Month - 1];
      Date_DayMonAfterTomorrow = DateFormatter.weekdays_short[(int)afterTomorrow.DayOfWeek] + ", " + afterTomorrow.Day.ToString() + " " + DateFormatter.monthes_short[afterTomorrow.Month - 1];
    }
  }
}
