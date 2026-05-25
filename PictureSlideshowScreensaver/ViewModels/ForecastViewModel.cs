using System;
using System.Collections.Generic;
using informers;
using Yaps.Core.Abstractions;
using Yaps.Core.Models.Weather;

namespace PictureSlideshowScreensaver.ViewModels
{
  /// <summary>
  /// Stage 6.2b: root view-model for the weather widget tree. Owns one
  /// <see cref="WeatherInformer"/> per <see cref="WeatherPeriod"/> the
  /// UI exposes (live + 12 forecast slots), each pre-configured for its
  /// period so the <see cref="presenters.Weather"/> UserControl no longer
  /// has to reach into the container to fetch an informer for itself.
  /// Pushed into the visual tree via DataContext from
  /// <see cref="ScreensaverViewModel"/>; per-tile binding sources are
  /// the properties below.
  /// </summary>
  public sealed class ForecastViewModel : IDisposable
  {
    private readonly List<WeatherInformer> _all = new();
    private bool _disposed;

    public WeatherInformer Now { get; }
    public WeatherInformer TodayMorning { get; }
    public WeatherInformer TodayDay { get; }
    public WeatherInformer TodayEvening { get; }
    public WeatherInformer TodayNight { get; }
    public WeatherInformer TomorrowMorning { get; }
    public WeatherInformer TomorrowDay { get; }
    public WeatherInformer TomorrowEvening { get; }
    public WeatherInformer TomorrowNight { get; }
    public WeatherInformer DayAfterTomorrowMorning { get; }
    public WeatherInformer DayAfterTomorrowDay { get; }
    public WeatherInformer DayAfterTomorrowEvening { get; }
    public WeatherInformer DayAfterTomorrowNight { get; }

    public ForecastViewModel(IWeatherSnapshotStore store)
    {
      Now = Make(store, WeatherPeriod.Now);
      TodayMorning = Make(store, WeatherPeriod.TodayMorning);
      TodayDay = Make(store, WeatherPeriod.TodayDay);
      TodayEvening = Make(store, WeatherPeriod.TodayEvening);
      TodayNight = Make(store, WeatherPeriod.TodayNight);
      TomorrowMorning = Make(store, WeatherPeriod.TomorrowMorning);
      TomorrowDay = Make(store, WeatherPeriod.TomorrowDay);
      TomorrowEvening = Make(store, WeatherPeriod.TomorrowEvening);
      TomorrowNight = Make(store, WeatherPeriod.TomorrowNight);
      DayAfterTomorrowMorning = Make(store, WeatherPeriod.DayAfterTomorrowMorning);
      DayAfterTomorrowDay = Make(store, WeatherPeriod.DayAfterTomorrowDay);
      DayAfterTomorrowEvening = Make(store, WeatherPeriod.DayAfterTomorrowEvening);
      DayAfterTomorrowNight = Make(store, WeatherPeriod.DayAfterTomorrowNight);
    }

    private WeatherInformer Make(IWeatherSnapshotStore store, WeatherPeriod period)
    {
      var i = new WeatherInformer(store) { Weather_Period = period };
      _all.Add(i);
      return i;
    }

    public void Dispose()
    {
      if (_disposed)
        return;
      _disposed = true;

      // Each informer subscribes to store.Updated + owns a DispatcherTimer.
      // Without Close() the timer closure (and the store subscription) keep
      // the VM and ultimately the photo-frame window alive past close.
      foreach (var i in _all)
        i.Close();
    }
  }
}
