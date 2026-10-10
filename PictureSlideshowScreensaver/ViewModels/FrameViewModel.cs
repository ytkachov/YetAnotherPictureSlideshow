using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Serilog;
using Yaps.Core.Video;

namespace PictureSlideshowScreensaver.ViewModels
{

  public class SimpleCommand : ICommand
  {
    private Action<object> _action;
    private bool _canExecute;

    public SimpleCommand(Action<object> action, bool canExecute = true)
    {
      _action = action;
      _canExecute = canExecute;
    }

    public bool Active
    {
      get
      {
        return _canExecute;
      }
      set
      {
        _canExecute = value;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
      }
    }

    public bool CanExecute(object parameter)
    {
      return _canExecute;
    }

    public event EventHandler CanExecuteChanged;
    public void Execute(object parameter)
    {
      _action(parameter);
    }
  }


  public class FrameViewModel : BaseViewModel
  {
    // How long a finished Live Photo movie takes to dissolve into its still.
    // Short on purpose: the movie's last frame and the still are nearly the
    // same picture, so this only has to hide the small difference between
    // them, not be a transition of its own.
    private static readonly TimeSpan LiveRevealTime = TimeSpan.FromMilliseconds(600);

    private bool _isActive;
    private Random _rand;
    private Stretch _imageStretch;
    private BitmapImage _imageSource;
    private string _videoSource;
    private bool _imageVisible;
    private bool _videoVisible;
    private Stretch _videoStretch = Stretch.Uniform;
    private string _frameName;
    private Grid _gridControl;    // animation parameters can not be set in xaml
    private double _videoRotationAngle;

    private ScaleTransform _scaleTransform;
    private DoubleAnimation _fadeAnimation;
    private DoubleAnimation _scaleAnimation;


    private ICommand _onGridLoaded;
    private ICommand _onVideoLoaded;
    private ICommand _onVideoOpened;
    private ICommand _onVideoEnded;
    private ICommand _onVideoFailed;

    // The player itself, handed over by the view's Loaded trigger. A clip has
    // to be seeked to its start offset and a Live Photo movie faded out, which
    // the bindings can't express, so this frame drives the element directly —
    // the same arrangement the Grid already uses for the Ken Burns animations.
    private MediaElement _videoElement;

    // What the player is doing for this frame. At most one is non-null:
    // _playingClip — a standalone video, the whole item;
    // _liveVideo   — a Live Photo movie playing over its still, which is
    //                revealed when the movie ends.
    private VideoClip _playingClip;
    private VideoClip _liveVideo;
    private TimeSpan _liveRevealTime;

    // The dissolve currently running, so a dissolve that completes after the
    // frame has moved on to another item doesn't hide the new item's video.
    private DoubleAnimation _revealAnimation;

    private readonly double _videoVolume;

    public bool IsActive { get { return _isActive; } set { _isActive = value; RaisePropertyChanged(); } }
    public Stretch ImageStretch { get { return _imageStretch; } set { _imageStretch = value; RaisePropertyChanged(); } }
    public BitmapImage ImageSource { get { return _imageSource; } set { _imageSource = value; RaisePropertyChanged(); } }
    public string VideoSource { get { return _videoSource; } set { _videoSource = value; RaisePropertyChanged(); } }
    public bool ImageVisible { get { return _imageVisible; } set { _imageVisible = value; RaisePropertyChanged(); } }
    public bool VideoVisible { get { return _videoVisible; } set { _videoVisible = value; RaisePropertyChanged(); } }
    public Stretch VideoStretch { get { return _videoStretch; } set { _videoStretch = value; RaisePropertyChanged(); } }
    public double VideoRotationAngle { get { return _videoRotationAngle; } set { _videoRotationAngle = value; RaisePropertyChanged(); } }
    public string FrameName => _frameName;

    // Bound to the player's Volume. 0 keeps the frame silent, which is the
    // default — see Settings.VideoVolume.
    public double VideoVolume => _videoVolume;

    public ICommand OnGridLoaded => _onGridLoaded;
    public ICommand OnVideoLoaded => _onVideoLoaded;
    public ICommand OnVideoOpened => _onVideoOpened;
    public ICommand OnVideoEnded => _onVideoEnded;
    public ICommand OnVideoFailed => _onVideoFailed;

    public FrameViewModel(string frame_name, int videoVolumePercent = 0)
    {
      _frameName = frame_name;
      _rand = new Random(DateTime.Now.Millisecond);
      _videoVolume = Math.Clamp(videoVolumePercent, 0, 100) / 100.0;
      _onGridLoaded = new SimpleCommand((grid) => GridLoaded(grid));
      _onVideoLoaded = new SimpleCommand((video) => _videoElement = video as MediaElement ?? _videoElement);
      _onVideoOpened = new SimpleCommand((video) => VideoOpened(video));
      _onVideoEnded = new SimpleCommand((video) => VideoEnded());
      _onVideoFailed = new SimpleCommand((args) => VideoFailed(args as ExceptionRoutedEventArgs));
    }

    public void Activate(ImageInfo nextphoto, TimeSpan fadetime, TimeSpan movetime, bool accented, BitmapImage prebuiltBitmap = null)
    {
      if (nextphoto.clip != null)
      {
        ActivateClip(nextphoto.clip);
        return;
      }

      // Whatever this frame played last time it was on screen is let go
      // before the new photo goes in.
      StopVideo();

      SetImage(nextphoto, movetime, accented, prebuiltBitmap);
      ImageVisible = true;
      _fadeAnimation = new DoubleAnimation(0.0, 1.0, fadetime);

      if (nextphoto.live_video is { } live)
      {
        ActivateLive(live, fadetime);
        return;
      }

      ResetGrid(opacity: 0.0);
      StartFadeIn();
      StartKenBurns();
      IsActive = true;
    }

    public void Deactivate(TimeSpan fadetime)
    {
      IsActive = false;

      // Only pause: the frame now sits under the incoming one, which is still
      // fading in over it, so hiding the video here would flash black behind
      // the fade. The file is released when this frame is next activated.
      PauseVideo();
    }

    // Plays one slice of a standalone video: no bitmap, no Ken Burns, no fade
    // — the moving picture is its own transition.
    private void ActivateClip(VideoClip clip)
    {
      _fadeAnimation = null;
      _scaleAnimation = null;
      _scaleTransform = null;
      _liveVideo = null;
      _revealAnimation = null;

      ImageSource = null;
      ImageVisible = false;
      ResetGrid(opacity: 1.0);
      ResetVideoOpacity();
      VideoStretch = Stretch.Uniform;
      VideoRotationAngle = clip.RotationDegrees;
      VideoVisible = true;
      _playingClip = clip;

      // Assigning the same path again does not raise PropertyChanged, so the
      // player would never re-open the media and MediaOpened would never
      // fire. When the file is already open we seek straight away; otherwise
      // the MediaOpened trigger picks it up.
      bool alreadyOpen = _videoElement != null
                         && _videoElement.Source != null
                         && _videoElement.NaturalDuration.HasTimeSpan
                         && string.Equals(_videoElement.Source.LocalPath, clip.Path, StringComparison.OrdinalIgnoreCase);

      VideoSource = clip.Path;

      if (alreadyOpen)
        StartClip();
    }

    // A Live Photo: the movie plays over its own still in exactly the same
    // geometry — same stretch as the photo, rotation from the movie's own
    // track matrix, no zoom — so when it ends and dissolves away the still
    // underneath is the same picture, and the Ken Burns pan starts from 1.0x
    // with nothing jumping.
    private void ActivateLive(VideoClip live, TimeSpan fadetime)
    {
      _liveVideo = live;
      _liveRevealTime = fadetime == TimeSpan.Zero ? TimeSpan.Zero : LiveRevealTime;

      // Invisible until the movie's first frame is ready. The frame goes on
      // top right away (IsActive) — at opacity 0 the previous photo keeps
      // showing through, instead of the new still flashing before its movie.
      ResetGrid(opacity: 0.0);
      ResetVideoOpacity();
      VideoStretch = ImageStretch;
      VideoRotationAngle = live.RotationDegrees;
      VideoVisible = true;
      VideoSource = live.Path;   // StopVideo cleared it, so this always re-opens
      IsActive = true;
    }

    private void StartClip()
    {
      var player = _videoElement;
      var clip = _playingClip;
      if (player == null || clip == null)
        return;

      try
      {
        player.Position = clip.Start;
        player.Play();
      }
      catch (Exception ex)
      {
        // A clip that won't play must not take the slideshow down; the next
        // tick moves on to another item.
        Log.Error(ex, "Could not start {Video} at {Start}", clip.Path, clip.Start);
      }

      IsActive = true;
    }

    // The frame fades in on the movie's first frame, held still, and the
    // movie only starts once the fade is done — a Live Photo movie is about
    // as long as the fade itself, so playing it underneath would spend most
    // of it half-transparent.
    private void StartLive()
    {
      var player = _videoElement;
      var live = _liveVideo;
      if (player == null || live == null)
        return;

      try
      {
        player.Position = TimeSpan.Zero;
        player.Play();
        player.Pause();   // Manual mode renders nothing until played; this shows frame one
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "Could not start Live Photo movie {Video}", live.Path);
        RevealStill(immediately: true);
        return;
      }

      if (_fadeAnimation == null || _fadeAnimation.Duration.TimeSpan == TimeSpan.Zero)
      {
        StartFadeIn();
        PlayLive(live);
        return;
      }

      _fadeAnimation.Completed += (_, _) => PlayLive(live);
      StartFadeIn();
    }

    private void PlayLive(VideoClip live)
    {
      // The frame may have moved on while the fade ran.
      if (!ReferenceEquals(_liveVideo, live))
        return;

      try
      {
        _videoElement?.Play();
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "Could not play Live Photo movie {Video}", live.Path);
        RevealStill(immediately: true);
      }
    }

    // The movie is over (or never started): hand the frame to the still.
    private void RevealStill(bool immediately)
    {
      if (_liveVideo == null)
        return;   // already revealed — MediaEnded and MediaFailed can both arrive
      _liveVideo = null;

      // If the movie never got as far as opening, the frame is still at
      // opacity 0 — the still has to fade in the way a plain photo would.
      if (_gridControl != null && _gridControl.Opacity < 1.0 && !_gridControl.HasAnimatedProperties)
        StartFadeIn();

      StartKenBurns();

      var player = _videoElement;
      if (immediately || player == null || _liveRevealTime == TimeSpan.Zero)
      {
        HideVideo();
        return;
      }

      // Hold the last frame and dissolve it into the still.
      try
      {
        player.Pause();
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "Could not pause finished Live Photo movie");
      }

      var dissolve = new DoubleAnimation(1.0, 0.0, _liveRevealTime);
      dissolve.Completed += (_, _) =>
      {
        if (ReferenceEquals(_revealAnimation, dissolve))
          HideVideo();
      };
      _revealAnimation = dissolve;
      player.BeginAnimation(UIElement.OpacityProperty, dissolve);
    }

    private void HideVideo()
    {
      _revealAnimation = null;
      VideoVisible = false;
      try
      {
        _videoElement?.Stop();
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "Could not stop video playback");
      }

      VideoSource = null;
    }

    private void StopVideo()
    {
      bool hadVideo = _playingClip != null || _liveVideo != null || VideoSource != null;
      _playingClip = null;
      _liveVideo = null;
      if (hadVideo)
        HideVideo();
      else
        _revealAnimation = null;
      VideoVisible = false;
    }

    private void PauseVideo()
    {
      if (VideoSource == null)
        return;

      try
      {
        _videoElement?.Pause();
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "Could not pause video playback");
      }
    }

    // The Ken Burns animation and the fade both hold their end values, so a
    // reused frame still carries the previous photo's zoom (up to 1.4x) and
    // opacity. Clearing the animations first is what lets the local values
    // take effect again.
    private void ResetGrid(double opacity)
    {
      if (_gridControl == null)
        return;

      _gridControl.BeginAnimation(Grid.OpacityProperty, null);
      _gridControl.Opacity = opacity;
      _gridControl.RenderTransform = Transform.Identity;
    }

    // Same reason: the last dissolve left the player at opacity 0.
    private void ResetVideoOpacity()
    {
      if (_videoElement == null)
        return;

      _videoElement.BeginAnimation(UIElement.OpacityProperty, null);
      _videoElement.Opacity = 1.0;
    }

    // prebuiltBitmap is supplied when ScreensaverViewModel pre-decoded this
    // photo's full pipeline (bitmap + orientation + faces) during the prior
    // photo's display. Reusing it avoids a second JPEG decode on the UI
    // thread, which is the whole point of the prefetch path.
    private void SetImage(ImageInfo nextphoto, TimeSpan movetime, bool accented, BitmapImage prebuiltBitmap = null)
    {
      BitmapImage bmp_img = prebuiltBitmap ?? nextphoto.bitmap;

      ImageStretch = Stretch.Uniform;
      if (bmp_img.Width > bmp_img.Height * 1.2)
        ImageStretch = Stretch.UniformToFill;

      ImageSource = bmp_img;
      _scaleTransform = null;
      _scaleAnimation = null;
      if (movetime != TimeSpan.MinValue && _gridControl != null)
      {
        double cx = _gridControl.ActualWidth / 2;
        double cy = _gridControl.ActualHeight / 2;
        double cs = 1.0 + 0.4 * _rand.NextDouble();

        if (accented && nextphoto.accent_count != 0)
        {
          double dc = 1;
          dc = bmp_img.PixelHeight / _gridControl.ActualHeight;
          var accent = nextphoto.accent;

          cx += accent.X / dc;
          cy += accent.Y / dc;

          cs = 1.05 + 0.8 * _rand.NextDouble();
        }

        _scaleTransform = new ScaleTransform(1.0, 1.0, cx, cy);
        _scaleAnimation = new DoubleAnimation(cs, new Duration(movetime));
      }
    }

    private void GridLoaded(object gridControl)
    {
      _gridControl = gridControl as Grid;
    }

    private void VideoOpened(object videoControl)
    {
      _videoElement = videoControl as MediaElement ?? _videoElement;

      if (_playingClip != null)
        StartClip();
      else if (_liveVideo != null)
        StartLive();
    }

    private void VideoEnded()
    {
      if (_playingClip != null)
      {
        // A clip whose slice reaches the end of the file: hold the last frame
        // until the slideshow switches, rather than revealing the still image
        // that a standalone video doesn't have.
        try
        {
          _videoElement?.Pause();
        }
        catch (Exception ex)
        {
          Log.Warning(ex, "Could not pause finished clip");
        }

        return;
      }

      RevealStill(immediately: false);
    }

    private void VideoFailed(ExceptionRoutedEventArgs args)
    {
      Log.Warning(args?.ErrorException, "Could not play {Video}", VideoSource);

      // A Live Photo falls back to its still; a broken standalone clip just
      // leaves the frame black until the next tick moves on.
      RevealStill(immediately: true);
    }

    private void StartFadeIn()
    {
      if (_gridControl != null && _fadeAnimation != null)
        _gridControl.BeginAnimation(Grid.OpacityProperty, _fadeAnimation);
    }

    private void StartKenBurns()
    {
      if (_gridControl != null && _scaleAnimation != null && _scaleTransform != null)
      {
        _scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, _scaleAnimation);
        _scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, _scaleAnimation);

        _gridControl.RenderTransform = _scaleTransform;
      }
    }

  }
}
