using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using PictureSlideshowScreensaver.ViewModels;

class dateformatter
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

    public string PhotoDescription { get { return _date_taken; } set { _date_taken = value; RaisePropertyChanged(); } }
    public string FacesFound { get { return _faces_found; } }
    public void SetFacesFound(int num)
    {
        _faces_found = "";
        for (int i = 0; i < num; i++)
          _faces_found += "\u263B";

        RaisePropertyChanged("FacesFound");
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
    private DispatcherTimer _clockTick = new DispatcherTimer();

    public DateTimeInformer()
    {
      _clockTick.Tick += new EventHandler(clock_Tick);
      _clockTick.Interval = TimeSpan.FromSeconds(1.0);
      _clockTick.Start();
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
      Time_Tick = 1 - Time_Tick;
      Time_Hours = DateTime.Now.Hour.ToString("D2");
      Time_Minutes = DateTime.Now.Minute.ToString("D2");
      Time_Seconds = DateTime.Now.Second.ToString("D2");

      Date_Full = dateformatter.weekdays_short[(int)DateTime.Now.DayOfWeek] + ", " + (DateTime.Now.Day).ToString() + " " + dateformatter.monthes_short[DateTime.Now.Month - 1];
      Date_DayMon = dateformatter.weekdays_short[(int)DateTime.Now.DayOfWeek] + ", " + (DateTime.Now.Day).ToString() + " " + dateformatter.monthes_short[DateTime.Now.Month - 1];
      Date_DayMonTomorrow = dateformatter.weekdays_short[(int)DateTime.Now.AddDays(1).DayOfWeek] + ", " + (DateTime.Now.AddDays(1).Day).ToString() + " " + dateformatter.monthes_short[DateTime.Now.AddDays(1).Month - 1];
      Date_DayMonAfterTomorrow = dateformatter.weekdays_short[(int)DateTime.Now.AddDays(2).DayOfWeek] + ", " + (DateTime.Now.AddDays(2).Day).ToString() + " " + dateformatter.monthes_short[DateTime.Now.AddDays(2).Month - 1];
    }
  }
}
