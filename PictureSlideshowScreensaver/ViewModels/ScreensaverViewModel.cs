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
using weather;

namespace PictureSlideshowScreensaver.ViewModels
{

  public class ScreensaverViewModel : BaseViewModel
  {
    private Settings _settings = new Settings();

    private ImagesProvider _images = new LocalImages();
    private DispatcherTimer _switchImage;

    private PhotoProperties _photo_properties;
    private FrameViewModel _firstImage;
    private FrameViewModel _secondImage;

    private int _prevTime = 0;
    private bool _isNightTime = false;

    public PhotoProperties PhotoProperties { get { return _photo_properties; } set { _photo_properties = value; RaisePropertyChanged(); } }
    public FrameViewModel FirstImage { get { return _firstImage; } set { _firstImage = value; RaisePropertyChanged(); } }
    public FrameViewModel SecondImage { get { return _secondImage; } set { _secondImage = value; RaisePropertyChanged(); } }

    public ScreensaverViewModel()
    {
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

    void fade_Tick(object sender, EventArgs e)
    {
      _switchImage.Interval = TimeSpan.FromSeconds(_settings._updateInterval);

      _isNightTime = DateTime.Now.Hour < 7 || DateTime.Now.Hour >= 23;
      //_isNightTime = true;
      if ((!_settings._workAtNight) && _isNightTime)
        return;        // фотографии не меняются ночью.

      NextImage();
    }

    private void NextImage()
    {
      // write stat every day at 8PM
      if (_settings._writeStat && _prevTime == 20 && DateTime.Now.Hour == _prevTime + 1)
        _images.WriteStat(_settings._writeStatPath);

      _prevTime = DateTime.Now.Hour;

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
          Console.WriteLine("ERROR: " + ex.Message);
        }
      }
    }
  }
}
