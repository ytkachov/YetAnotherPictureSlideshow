using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using informers;
using PictureSlideshowScreensaver.Models;
using presenters;
using Serilog;
using Yaps.Core.Abstractions;

namespace PictureSlideshowScreensaver.ViewModels
{

  public partial class ScreensaverViewModel : BaseViewModel, IDisposable
  {
    private readonly Settings _settings;
    private readonly ImagesProvider _images;
    private readonly IClock _clock;
    private readonly Dispatcher _dispatcher;
    private DispatcherTimer _switchImage;

    private PhotoProperties _photo_properties;
    private FrameViewModel _firstImage;
    private FrameViewModel _secondImage;

    private int _prevTime = 0;
    private bool _isNightTime = false;
    private bool _disposed;

    // Scan-overlay state. The "pending" fields are written on the scan thread
    // and read back when the throttled dispatcher callback fires; the public
    // properties are only ever touched on the UI thread.
    private bool _isScanning = true;
    private int _scanFileCount;
    private string _scanFolder;
    private volatile int _pendingCount;
    private volatile string _pendingFolder;
    private long _lastScanUiTicks;

    public PhotoProperties PhotoProperties { get { return _photo_properties; } set { _photo_properties = value; RaisePropertyChanged(); } }
    public FrameViewModel FirstImage { get { return _firstImage; } set { _firstImage = value; RaisePropertyChanged(); } }
    public FrameViewModel SecondImage { get { return _secondImage; } set { _secondImage = value; RaisePropertyChanged(); } }

    public bool IsScanning
    {
      get { return _isScanning; }
      set { _isScanning = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ScanOverlayVisibility)); }
    }

    public Visibility ScanOverlayVisibility => _isScanning ? Visibility.Visible : Visibility.Collapsed;

    public int ScanFileCount { get { return _scanFileCount; } set { _scanFileCount = value; RaisePropertyChanged(); } }
    public string ScanFolder { get { return _scanFolder; } set { _scanFolder = value; RaisePropertyChanged(); } }

    // Bound to the forecast overlay's visibility; toggled by the F key via
    // ToggleForecastCommand (see the window's InputBindings).
    [ObservableProperty]
    private bool _isForecastVisible;

    [RelayCommand]
    private void ToggleForecast() => IsForecastVisible = !IsForecastVisible;

    [RelayCommand]
    private static void Exit() => Application.Current.Shutdown();

    public ScreensaverViewModel(Settings settings, ImagesProvider images, IClock clock)
    {
      _settings = settings;
      _images = images;
      _clock = clock;
      _dispatcher = Dispatcher.CurrentDispatcher;

      // Subscribe before init() kicks off the background scan so we don't miss
      // the early progress events on a slow share.
      _images.ScanProgressChanged += OnScanProgress;
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

      _images.ScanProgressChanged -= OnScanProgress;

      // Stop the timer and unsubscribe so the closure capturing 'this'
      // doesn't keep the VM alive after the window closes.
      if (_switchImage != null)
      {
        _switchImage.Stop();
        _switchImage.Tick -= fade_Tick;
        _switchImage = null;
      }
    }

    // Fires on the scan thread, potentially once per file. Stash the latest
    // values and only marshal to the UI at ~4 Hz so a fast SMB enumeration
    // doesn't flood the dispatcher.
    private void OnScanProgress(object sender, ScanProgress e)
    {
      if (_disposed)
        return;

      _pendingCount = e.FilesFound;
      _pendingFolder = e.CurrentFolder;

      long now = Environment.TickCount64;
      if (now - Interlocked.Read(ref _lastScanUiTicks) < 250)
        return;
      Interlocked.Exchange(ref _lastScanUiTicks, now);

      _dispatcher.BeginInvoke(new Action(() =>
      {
        if (_disposed || !IsScanning)
          return;
        ScanFileCount = _pendingCount;
        ScanFolder = ShortenFolder(_pendingFolder);
      }));
    }

    // Keep only the trailing part of a long network path so the overlay text
    // stays on one line, e.g. "…\share\PHOTOS\2023".
    private static string ShortenFolder(string folder)
    {
      const int max = 60;
      if (string.IsNullOrEmpty(folder) || folder.Length <= max)
        return folder;
      return "…" + folder.Substring(folder.Length - (max - 1));
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
      if (nextphoto == null)
        return;

      // EXIF (orientation / date / GPS) is read lazily and off the UI thread —
      // on a network share it can pull the whole file. The orientation and the
      // on-screen date caption are both consumed during activation, so we wait
      // for the read before marshalling the activation back to the dispatcher.
      Task.Run(() =>
      {
        try
        {
          nextphoto.EnsureMetadataLoaded();
        }
        catch (Exception ex)
        {
          Log.Error(ex, "Metadata load failed");
        }

        if (_disposed)
          return;

        _dispatcher.BeginInvoke(new Action(() => ActivatePhoto(nextphoto)));
      });
    }

    private void ActivatePhoto(ImageInfo nextphoto)
    {
      if (_disposed)
        return;

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

        // First real photo on screen — drop the scanning overlay.
        if (IsScanning)
          IsScanning = false;
      }
      catch (Exception ex)
      {
        Log.Error(ex, "NextImage failed for {Desc}", nextphoto?.description);
      }
    }
  }
}
