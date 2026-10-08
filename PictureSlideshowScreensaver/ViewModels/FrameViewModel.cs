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
    private bool _isActive;
    private Random _rand;
    private Stretch _imageStretch;
    private BitmapImage _imageSource;
    private string _videoSource;
    private bool _imageVisible;
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

    // The player itself, handed over by the view's Loaded trigger. A clip has
    // to be seeked to its start offset, which the binding can't express, so
    // this frame drives the element directly — the same arrangement the Grid
    // already uses for the Ken Burns animations.
    private MediaElement _videoElement;

    // The clip this frame is currently showing, or null when it is showing a
    // photo. Doubles as the flag that tells MediaEnded / Deactivate they are
    // dealing with a standalone clip rather than a photo's companion video.
    private VideoClip _playingClip;
    private readonly double _videoVolume;

    public bool IsActive { get { return _isActive; } set { _isActive = value; RaisePropertyChanged(); } }
    public Stretch ImageStretch { get { return _imageStretch; } set { _imageStretch = value; RaisePropertyChanged(); } }
    public BitmapImage ImageSource { get { return _imageSource; } set { _imageSource = value; RaisePropertyChanged(); } }
    public string VideoSource { get { return _videoSource; } set { _videoSource = value; RaisePropertyChanged(); } }
    public bool ImageVisible { get { return _imageVisible; } set { _imageVisible = value; RaisePropertyChanged(); } }
    public double VideoRotationAngle { get { return _videoRotationAngle; } set { _videoRotationAngle = value; RaisePropertyChanged(); } }
    public string FrameName => _frameName;

    // Bound to the player's Volume. 0 keeps the frame silent, which is the
    // default — see Settings.VideoVolume.
    public double VideoVolume => _videoVolume;

    public ICommand OnGridLoaded => _onGridLoaded;
    public ICommand OnVideoLoaded => _onVideoLoaded;
    public ICommand OnVideoOpened => _onVideoOpened;
    public ICommand OnVideoEnded => _onVideoEnded;

    public FrameViewModel(string frame_name, int videoVolumePercent = 0)
    {
      _frameName = frame_name;
      _rand = new Random(DateTime.Now.Millisecond);
      _videoVolume = Math.Clamp(videoVolumePercent, 0, 100) / 100.0;
      _onGridLoaded = new SimpleCommand((grid) => GridLoaded(grid));
      _onVideoLoaded = new SimpleCommand((video) => _videoElement = video as MediaElement ?? _videoElement);
      _onVideoOpened = new SimpleCommand((video) => VideoOpened(video));
      _onVideoEnded = new SimpleCommand((video) => VideoEnded());
    }

    public void Activate(ImageInfo nextphoto, TimeSpan fadetime, TimeSpan movetime, bool accented, BitmapImage prebuiltBitmap = null)
    {
      if (nextphoto.clip != null)
      {
        ActivateClip(nextphoto.clip);
        return;
      }

      // Coming back to photos after a clip: let go of the player before the
      // still image is revealed.
      StopClip();

      SetImage(nextphoto, movetime, accented, prebuiltBitmap);

      if (!nextphoto.has_accompanying_video)
      {
        ImageVisible = true;
        _fadeAnimation = new DoubleAnimation(0.0, 1.0, fadetime);

        StartImage();
        IsActive = true;
      }
      else
      {
        _fadeAnimation = null;

        ImageVisible = false;
        if (nextphoto.orientation == RotateFlipType.Rotate180FlipNone || nextphoto.orientation == RotateFlipType.Rotate180FlipX ||
            nextphoto.orientation == RotateFlipType.Rotate180FlipXY || nextphoto.orientation == RotateFlipType.Rotate180FlipY)
          VideoRotationAngle = 180.0;
        else if (nextphoto.orientation == RotateFlipType.Rotate270FlipNone || nextphoto.orientation == RotateFlipType.Rotate270FlipX ||
                 nextphoto.orientation == RotateFlipType.Rotate270FlipXY || nextphoto.orientation == RotateFlipType.Rotate270FlipY)
          VideoRotationAngle = 90.0; // this is correct!
        else if (nextphoto.orientation == RotateFlipType.Rotate90FlipNone || nextphoto.orientation == RotateFlipType.Rotate90FlipX ||
                 nextphoto.orientation == RotateFlipType.Rotate90FlipXY || nextphoto.orientation == RotateFlipType.Rotate90FlipY)
          VideoRotationAngle = 90.0;

        VideoSource = nextphoto.video_name;
      }
    }

    public void Deactivate(TimeSpan fadetime)
    {
      IsActive = false;

      // The frame is off screen now, so release the file and the decoder
      // rather than leaving a handle open on the share.
      StopClip();
    }

    // Plays one slice of a standalone video: no bitmap, no Ken Burns, no fade
    // — the moving picture is its own transition.
    private void ActivateClip(VideoClip clip)
    {
      _fadeAnimation = null;
      _scaleAnimation = null;
      _scaleTransform = null;

      ImageSource = null;
      ImageVisible = false;          // reveals the MediaElement
      VideoRotationAngle = clip.RotationDegrees;
      _playingClip = clip;

      // The Ken Burns animation holds its end value, so this frame's Grid is
      // still carrying the zoom (up to 1.4x) and opacity of the last photo it
      // showed. Left alone, the clip would play scaled and cropped. Clearing
      // the animation first is what lets the local values take effect again.
      if (_gridControl != null)
      {
        _gridControl.BeginAnimation(Grid.OpacityProperty, null);
        _gridControl.Opacity = 1.0;
        _gridControl.RenderTransform = Transform.Identity;
      }

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

    private void StopClip()
    {
      if (_playingClip == null)
        return;

      _playingClip = null;
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
      {
        StartClip();
        return;
      }

      // A photo's companion video. LoadedBehavior is Manual (the clip path
      // needs to seek), so playback starts here instead of by itself.
      try
      {
        _videoElement?.Play();
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "Could not start companion video");
      }

      IsActive = true;
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

      StartImage();
    }

    private void StartImage()
    {
      ImageVisible = true;
      if (_gridControl != null)
      {
        if (_fadeAnimation != null)
          _gridControl.BeginAnimation(Grid.OpacityProperty, _fadeAnimation);

        if (_scaleAnimation != null && _scaleTransform != null)
        {
          _scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, _scaleAnimation);
          _scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, _scaleAnimation);

          _gridControl.RenderTransform = _scaleTransform;
        }
      }
    }

  }
}
