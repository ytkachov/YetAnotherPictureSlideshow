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
    private ICommand _onVideoOpened;
    private ICommand _onVideoEnded;

    public bool IsActive { get { return _isActive; } set { _isActive = value; RaisePropertyChanged(); } }
    public Stretch ImageStretch { get { return _imageStretch; } set { _imageStretch = value; RaisePropertyChanged(); } }
    public BitmapImage ImageSource { get { return _imageSource; } set { _imageSource = value; RaisePropertyChanged(); } }
    public string VideoSource { get { return _videoSource; } set { _videoSource = value; RaisePropertyChanged(); } }
    public bool ImageVisible { get { return _imageVisible; } set { _imageVisible = value; RaisePropertyChanged(); } }
    public double VideoRotationAngle { get { return _videoRotationAngle; } set { _videoRotationAngle = value; RaisePropertyChanged(); } }
    public string FrameName => _frameName;

    public ICommand OnGridLoaded => _onGridLoaded;
    public ICommand OnVideoOpened => _onVideoOpened;
    public ICommand OnVideoEnded => _onVideoEnded;

    public FrameViewModel(string frame_name)
    {
      _frameName = frame_name;
      _rand = new Random(DateTime.Now.Millisecond);
      _onGridLoaded = new SimpleCommand((grid) => GridLoaded(grid));
      _onVideoOpened = new SimpleCommand((video) => VideoOpened(video));
      _onVideoEnded = new SimpleCommand((video) => StartImage());
    }

    public void Activate(ImageInfo nextphoto, TimeSpan fadetime, TimeSpan movetime, bool accented)
    {
      SetImage(nextphoto, movetime, accented);

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
    }

    private void SetImage(ImageInfo nextphoto, TimeSpan movetime, bool accented)
    {
      BitmapImage bmp_img = nextphoto.bitmap;

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
      IsActive = true;
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
