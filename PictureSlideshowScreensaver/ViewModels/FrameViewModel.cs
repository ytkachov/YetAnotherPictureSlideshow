using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private string _frameName;
    private Grid _gridControl;    // animation parameters can not be set 
    private ICommand _onGridLoaded;

    public bool IsActive { get { return _isActive; } set { _isActive = value; RaisePropertyChanged(); } }
    public Stretch ImageStretch { get { return _imageStretch; } set { _imageStretch = value; RaisePropertyChanged(); } }
    public BitmapImage ImageSource { get { return _imageSource; } set { _imageSource = value; RaisePropertyChanged(); } }
    public ICommand OnGridLoaded => _onGridLoaded;
    public string FrameName => _frameName;

    public FrameViewModel(string frame_name)
    {
      _frameName = frame_name;
      _rand = new Random(DateTime.Now.Millisecond);
      _onGridLoaded = new SimpleCommand((grid) => GridLoaded(grid));
    }

    public void Activate(ImageInfo nextphoto, TimeSpan fadetime, TimeSpan movetime, bool accented)
    {
      SetImage(nextphoto, movetime, accented);

      IsActive = true;

      if (_gridControl != null)
        _gridControl.BeginAnimation(Grid.OpacityProperty, new DoubleAnimation(0.0, 1.0, fadetime));
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

        ScaleTransform st = new ScaleTransform(1.0, 1.0, cx, cy);
        DoubleAnimation da = new DoubleAnimation(cs, new Duration(movetime));

        st.BeginAnimation(ScaleTransform.ScaleXProperty, da);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, da);

        _gridControl.RenderTransform = st;
      }
    }

    private void GridLoaded(object gridControl)
    {
      _gridControl = gridControl as Grid;
    }

  }
}
