using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using informers;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceProvider _services;
    private readonly Dispatcher _dispatcher;
    private readonly ForecastViewModel _forecast;
    private DispatcherTimer _switchImage;

    // Root of the weather widget tree. Pushed through DataContext from the
    // window's XAML (Stage 6.2b) so the W_now tile and the WeatherForecast
    // overlay no longer reach into App.Current.Services for an informer.
    public ForecastViewModel Forecast => _forecast;

    private PhotoProperties _photo_properties;
    private FrameViewModel _firstImage;
    private FrameViewModel _secondImage;

    private int _prevTime = 0;
    private bool _isNightTime = false;
    private bool _disposed;

    // Prefetch: the next photo's full bitmap pipeline (decode + ONNX
    // orientation + face detection) runs on a worker during the CURRENT
    // photo's display, so the dispatcher tick that switches photos no
    // longer blocks on it. Buffer is exactly one frame deep — enough to
    // hide the latency, cheap on memory. Only ever touched from the UI
    // thread: NextImage takes (Interlocked.Exchange), ActivatePhoto starts.
    private Task<PrefetchedPhoto> _prefetchTask;
    private CancellationTokenSource _prefetchCts;

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

    // Drives the live tile's "OM" / "Я" / "НГУ" source badge. Read once
    // from Settings at construction (Registry value WeatherShowProviderBadge);
    // the badge can't be toggled at runtime today.
    public bool ShowProviderBadge => _settings.WeatherShowProviderBadge;

    [RelayCommand]
    private void ToggleForecast() => IsForecastVisible = !IsForecastVisible;

    // Esc dismisses the forecast overlay first if it's open, only shuts
    // down the screensaver if nothing else can absorb the keypress.
    // Otherwise pressing F to peek at the forecast and Esc to dismiss it
    // would kill the appliance.
    [RelayCommand]
    private void Exit()
    {
      if (IsForecastVisible)
      {
        IsForecastVisible = false;
        return;
      }
      Application.Current.Shutdown();
    }

    // Opens the L-key log viewer. Modal — the slideshow keeps animating
    // behind it. Owner is set explicitly (not left to WPF's implicit
    // pick) for two reasons: it z-orders the modal above the slideshow
    // window even when both are Topmost, and combined with the dialog's
    // WindowStartupLocation="CenterOwner" it positions the dialog on
    // the same monitor the screensaver actually occupies (CenterScreen
    // would land it on the primary monitor, which on a multi-monitor
    // appliance may not be the photo frame). IServiceProvider rather
    // than a direct LogViewer ctor parameter so each press resolves a
    // fresh transient instance (re-reads the log file from scratch).
    [RelayCommand]
    private void ShowLog()
    {
      Log.Information("L pressed; opening log viewer");
      try
      {
        var viewer = _services.GetRequiredService<LogViewer>();
        var owner = Application.Current?.MainWindow;
        if (owner != null && !ReferenceEquals(owner, viewer))
          viewer.Owner = owner;
        viewer.ShowDialog();
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Failed to open log viewer");
      }
    }

    public ScreensaverViewModel(Settings settings, ImagesProvider images, IClock clock, IServiceProvider services, ForecastViewModel forecast)
    {
      _settings = settings;
      _images = images;
      _clock = clock;
      _services = services;
      _forecast = forecast;
      _dispatcher = Dispatcher.CurrentDispatcher;
      _prefetchCts = new CancellationTokenSource();

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

      // Cancel any in-flight prefetch. The bitmap pipeline has no
      // cancellation hooks, so a task already deep in JPEG decode / ONNX
      // inference will run to completion — its result is just discarded
      // and GC'd. The CTS lets the next prefetch check fail fast.
      if (_prefetchCts != null)
      {
        _prefetchCts.Cancel();
        _prefetchCts.Dispose();
        _prefetchCts = null;
      }
      _prefetchTask = null;

      // Stops the 13 informer DispatcherTimers and unsubscribes them from
      // IWeatherSnapshotStore.Updated; otherwise the timer closures keep
      // the VM alive past window close.
      _forecast?.Dispose();
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

      // Prefer the prefetched frame: its EXIF, JPEG decode, ONNX orientation
      // and face detection all already ran in the background during the
      // previous photo's display. Interlocked.Exchange so we only consume it
      // once — the next ActivatePhoto will start a new prefetch.
      var pending = Interlocked.Exchange(ref _prefetchTask, null);
      if (pending != null)
      {
        pending.ContinueWith(ConsumePrefetched, TaskContinuationOptions.ExecuteSynchronously);
        return;
      }

      LoadFreshPhoto();
    }

    // Fallback when no prefetch is sitting in the buffer — the very first
    // photo after startup, or a tick that fired before the previous
    // prefetch could complete (rare; the bitmap pipeline takes ~1 s and
    // the slideshow interval is many seconds).
    private void LoadFreshPhoto()
    {
      ImageInfo nextphoto = _images.GetNext();
      if (nextphoto == null)
        return;

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

        _dispatcher.BeginInvoke(new Action(() => ActivatePhoto(nextphoto, null)));
      });
    }

    // Runs on whichever thread completed the prefetch task (worker most of
    // the time, UI on the rare synchronous completion). Marshal the actual
    // activation back to the dispatcher.
    private void ConsumePrefetched(Task<PrefetchedPhoto> task)
    {
      if (_disposed)
        return;

      PrefetchedPhoto result = null;
      if (task.Status == TaskStatus.RanToCompletion)
        result = task.Result;
      else if (task.Exception != null)
        Log.Error(task.Exception, "Prefetch failed");

      if (result == null)
      {
        // Prefetch produced nothing (cancelled, faulted, or GetNext returned
        // null because the scan hadn't finished). Try a fresh load on the
        // UI thread's schedule.
        _dispatcher.BeginInvoke(new Action(LoadFreshPhoto));
        return;
      }

      _dispatcher.BeginInvoke(new Action(() => ActivatePhoto(result.Info, result.Bitmap)));
    }

    private void ActivatePhoto(ImageInfo nextphoto, BitmapImage prebuiltBitmap)
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
          FirstImage.Activate(nextphoto, ft, mt, acc, prebuiltBitmap);
          SecondImage.Deactivate(ft);
        }
        else
        {
          SecondImage.Activate(nextphoto, ft, mt, acc, prebuiltBitmap);
          FirstImage.Deactivate(ft);
        }

        PhotoProperties.SetFacesFound(nextphoto.accent_count);
        PhotoProperties.SetRotation(nextphoto.orientation);

        // First real photo on screen — drop the scanning overlay.
        if (IsScanning)
        {
          IsScanning = false;
          Log.Information("First photo shown");
        }
      }
      catch (Exception ex)
      {
        Log.Error(ex, "NextImage failed for {Desc}", nextphoto?.description);
      }
      finally
      {
        // Kick off preparation of the NEXT photo NOW, while this one is
        // being shown. Done in finally so an exception above still arms
        // the prefetch — otherwise a single bad photo would silently
        // disable prefetch for the rest of the session.
        StartPrefetch();
      }
    }

    // Schedules the next photo's pipeline (GetNext → EnsureMetadataLoaded →
    // bitmap getter, which covers JPEG decode + ONNX orientation + face
    // detection) on a worker thread. The bitmap getter freezes its result,
    // so the returned BitmapImage is safe to hand to the UI thread.
    //
    // Only one prefetch in flight at a time — the buffer is one frame deep.
    // Called only from the UI thread (ActivatePhoto), so the _prefetchTask
    // assignment doesn't need interlocking on the producer side; NextImage's
    // Interlocked.Exchange handles the consumer side.
    private void StartPrefetch()
    {
      if (_disposed)
        return;
      if (_prefetchTask != null)
        return;

      var ct = _prefetchCts.Token;
      _prefetchTask = Task.Run<PrefetchedPhoto>(() =>
      {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
          ct.ThrowIfCancellationRequested();
          var photo = _images.GetNext();
          if (photo == null)
            return null;

          photo.EnsureMetadataLoaded();
          ct.ThrowIfCancellationRequested();

          // This is the heavy bit: triggers the JPEG decode, the ONNX
          // orientation detection (only when needed), and Haar face
          // detection. All previously ran on the UI thread inside SetImage;
          // doing it here is the whole point of the prefetch.
          var bmp = photo.bitmap;
          sw.Stop();
          Log.Debug("Prefetched photo in {Ms} ms (faces={Faces}, rotation={Rotation})",
              sw.ElapsedMilliseconds, photo.accent_count, photo.orientation);
          return new PrefetchedPhoto(photo, bmp);
        }
        catch (OperationCanceledException)
        {
          return null;
        }
        catch (Exception ex)
        {
          Log.Error(ex, "Prefetch pipeline failed");
          return null;
        }
      }, ct);
    }

    private sealed class PrefetchedPhoto
    {
      public PrefetchedPhoto(ImageInfo info, BitmapImage bitmap)
      {
        Info = info;
        Bitmap = bitmap;
      }

      public ImageInfo Info { get; }
      public BitmapImage Bitmap { get; }
    }
  }
}
