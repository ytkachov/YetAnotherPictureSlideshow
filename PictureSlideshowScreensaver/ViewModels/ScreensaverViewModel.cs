using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using informers;
using PictureSlideshowScreensaver.Models;
using presenters;
using Serilog;
using weather;
using Yaps.Core.Abstractions;

namespace PictureSlideshowScreensaver.ViewModels
{

  public class ScreensaverViewModel : BaseViewModel, IDisposable
  {
    private readonly Settings _settings;
    private readonly ImagesProvider _images;
    private readonly IClock _clock;
    private DispatcherTimer _switchImage;

    private PhotoProperties _photo_properties;
    private FrameViewModel _firstImage;
    private FrameViewModel _secondImage;

    private int _prevTime = 0;
    private bool _isNightTime = false;
    private bool _disposed;

    public PhotoProperties PhotoProperties { get { return _photo_properties; } set { _photo_properties = value; RaisePropertyChanged(); } }
    public FrameViewModel FirstImage { get { return _firstImage; } set { _firstImage = value; RaisePropertyChanged(); } }
    public FrameViewModel SecondImage { get { return _secondImage; } set { _secondImage = value; RaisePropertyChanged(); } }

    public ScreensaverViewModel(Settings settings, ImagesProvider images, IClock clock)
    {
      _settings = settings;
      _images = images;
      _clock = clock;
      _images.init(new string[] { _settings._path, _settings._writeStat ? _settings._writeStatPath : "" });
      FirstImage = new FrameViewModel("one") { IsActive = true };
      SecondImage = new FrameViewModel("two") { IsActive = false };
      PhotoProperties = new PhotoProperties();

      NextImage(); // to show from the very start
      _switchImage = new DispatcherTimer();
      _switchImage.Interval = TimeSpan.FromSeconds(_settings._updateInterval);
      _switchImage.Tick += new EventHandler(fade_Tick);

      _switchImage.Start();
    }

    public void Dispose()
    {
      if (_disposed)
        return;
      _disposed = true;

      // Stop the timer and unsubscribe so the closure capturing 'this'
      // doesn't keep the VM alive after the window closes.
      if (_switchImage != null)
      {
        _switchImage.Stop();
        _switchImage.Tick -= fade_Tick;
        _switchImage = null;
      }
    }

    void fade_Tick(object sender, EventArgs e)
    {
      _switchImage.Interval = TimeSpan.FromSeconds(_settings._updateInterval);

      var hour = _clock.Now.Hour;
      _isNightTime = hour < 7 || hour >= 23;
      //_isNightTime = true;
      if ((!_settings._workAtNight) && _isNightTime)
        return;        // фотографии не меняются ночью.

      NextImage();
    }

    private void NextImage()
    {
      var hour = _clock.Now.Hour;
      // write stat every day at 8PM
      if (_settings._writeStat && _prevTime == 20 && hour == _prevTime + 1)
        _images.WriteStat(_settings._writeStatPath);

      _prevTime = hour;

      ImageInfo nextphoto = _images.GetNext();
      if (nextphoto != null)
      {
        try
        {
          PhotoProperties.PhotoDescription = nextphoto.description;
          var ft = TimeSpan.FromMilliseconds(_settings._noImageFading ||
                                             (_isNightTime && _settings._noNightImageFading)
                                            ? 0 : _settings._fadeSpeed);

          var mt = TimeSpan.MinValue;

          if (!(_settings._noImageScaling || (_isNightTime && _settings._noNightImageScaling)))
            mt = TimeSpan.FromSeconds(_settings._updateInterval);

          bool acc = !_settings._noImageAccents && !(_isNightTime && _settings._noNightImageAccents);

          if (!FirstImage.IsActive)
          {
            FirstImage.Activate(nextphoto, ft, mt, acc);
            SecondImage.Deactivate(ft);
          }
          else
          {
            SecondImage.Activate(nextphoto, ft, mt, acc);
            FirstImage.Deactivate(ft);
          }

          PhotoProperties.SetFacesFound(nextphoto.accent_count);
        }
        catch (Exception ex)
        {
          Log.Error(ex, "ERROR");
        }
      }
    }
  }
}
