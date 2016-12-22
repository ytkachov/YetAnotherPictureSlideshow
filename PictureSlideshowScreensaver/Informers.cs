using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows.Threading;

class dateformatter
{
  public static   string[] weekdays = { "Воскресенье", "Понедельник", "Вторник", "Среда", "Четверг", "Пятница", "Суббота" };
  internal static string[] weekdays_short = { "Вс", "Пн", "Вт", "Ср", "Чт", "Пт", "Сб" };
  internal static string[] monthes = { "Января", "Февраля", "Марта", "Апреля", "Мая", "Июня", "Июля", "Августа", "Сентября", "Октября", "Ноября", "Декабря" };
  internal static string[] monthes_short = { "Янв", "Фев", "Мар", "Апр", "Май", "Июн", "Июл", "Авг", "Сен", "Окт", "Ноя", "Дек" };
}

namespace informers
{

  class PhotoPropertiesInformer : INotifyPropertyChanged
  {
    private string _faces_found = "";
    private string _date_taken = "21/12/1997";

    public string Photo_Description { get { return _date_taken; } set { _date_taken = value; RaisePropertyChanged("Photo_Description"); } }
    public string Faces_Found { get { return _faces_found; } }
    public int Set_Faces_Found
    {
      set
      {
        _faces_found = "";
        for (int i = 0; i < value; i++)
          _faces_found += "\u263B";

        RaisePropertyChanged("Faces_Found");
      }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    private void RaisePropertyChanged(string propertyName)
    {
      if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
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

  class DateTimeInformer : INotifyPropertyChanged
  {
    private int    _time_Tick = 0;
    private string _time_Hours = "00";
    private string _time_Minutes = "00";
    private string _time_Seconds = "00";
    private string _date_Full = "Пт 01 Янв 2016";
    private DispatcherTimer _clockTick = new DispatcherTimer();

    public DateTimeInformer()
    {
      _clockTick.Tick += new EventHandler(clock_Tick);
      _clockTick.Interval = TimeSpan.FromSeconds(1.0);
      _clockTick.Start();
    }

    public string Time_Hours { get { return _time_Hours; } set { _time_Hours = value; RaisePropertyChanged("Time_Hours"); } }
    public string Time_Minutes { get { return _time_Minutes; } set { _time_Minutes = value; RaisePropertyChanged("Time_Minutes"); } }
    public string Time_Seconds { get { return _time_Seconds; } set { _time_Seconds = value; RaisePropertyChanged("Time_Seconds"); } }
    public string Date_Full { get { return _date_Full; } set { _date_Full = value; RaisePropertyChanged("Date_Full"); } }
    public int Time_Tick { get { return _time_Tick; } set { _time_Tick = value; RaisePropertyChanged("Time_Tick"); } }

    public event PropertyChangedEventHandler PropertyChanged;
    private void RaisePropertyChanged(string propertyName)
    {
      if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
    }

    void clock_Tick(object sender, EventArgs e)
    {
      Time_Tick = 1 - Time_Tick;
      Time_Hours = DateTime.Now.Hour.ToString("D2");
      Time_Minutes = DateTime.Now.Minute.ToString("D2");
      Time_Seconds = DateTime.Now.Second.ToString("D2");

      Date_Full = dateformatter.weekdays_short[(int)DateTime.Now.DayOfWeek] + ", " + (DateTime.Now.Day).ToString() + " " + dateformatter.monthes_short[DateTime.Now.Month - 1];
    }
  }
}
