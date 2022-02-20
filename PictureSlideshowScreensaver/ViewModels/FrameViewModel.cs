using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PictureSlideshowScreensaver.ViewModels
{
  public class FrameViewModel : BaseViewModel
  {
    private bool _isActive;
    private Random _rand;
    private Stretch _imageStretch;
    private BitmapImage _imageSource;

    public bool IsActive { get { return _isActive; } set { _isActive = value; RaisePropertyChanged(); } }
    public Stretch ImageStretch { get { return _imageStretch; } set { _imageStretch = value; RaisePropertyChanged(); } }
    public BitmapImage ImageSource { get { return _imageSource; } set { _imageSource = value; RaisePropertyChanged(); } }

    public FrameViewModel()
    {
      _rand = new Random(DateTime.Now.Millisecond);
    }

    public void Activate(ImageInfo nextphoto, TimeSpan fadetime, TimeSpan movetime, bool accented)
    {
      IsActive = true;
      SetImage(nextphoto, movetime, accented);

      //image.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(1, fadetime));
    }

    public void Deactivate(TimeSpan fadetime)
    {
      IsActive = false;
      // image.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(0, fadetime));
    }

    private void SetImage(ImageInfo nextphoto, TimeSpan movetime, bool accented)
    {
      BitmapImage bmp_img = nextphoto.bitmap;

      ImageStretch = Stretch.Uniform;
      if (bmp_img.Width > bmp_img.Height * 1.2)
        ImageStretch = Stretch.UniformToFill;

      ImageSource = bmp_img;
      //if (image.ActualHeight > 0 && image.ActualWidth > 0)
      //{
      //  // if image is valid
      //  if (movetime != TimeSpan.MinValue)
      //  {
      //    double cx = image.ActualWidth / 2;
      //    double cy = image.ActualHeight / 2;
      //    double cs = 1.0 + 0.1 * _rand.NextDouble();

      //    if (accented && nextphoto.accent_count != 0)
      //    {
      //      double dc = 1;
      //      dc = bmp_img.PixelHeight / image.ActualHeight;
      //      var accent = nextphoto.accent;

      //      cx += accent.X / dc;
      //      cy += accent.Y / dc;

      //      cs = 1.05 + 0.4 * _rand.NextDouble();
      //    }

      //    ScaleTransform st = new ScaleTransform(1.0, 1.0, cx, cy);
      //    DoubleAnimation da = new DoubleAnimation(cs, new Duration(movetime));

      //    st.BeginAnimation(ScaleTransform.ScaleXProperty, da);
      //    st.BeginAnimation(ScaleTransform.ScaleYProperty, da);

      //    image.RenderTransform = st;
      //  }
      //}
    }
  }
}
