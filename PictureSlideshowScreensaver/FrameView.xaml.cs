using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace presenters
{
  /// <summary>
  /// Interaction logic for FrameView.xaml
  /// </summary>
  public partial class FrameView : UserControl, INotifyPropertyChanged
  {
    private Random _rand;

    public FrameView()
    {
      _rand = new Random(DateTime.Now.Millisecond);
      InitializeComponent();
    }

    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register("Active", typeof(bool), typeof(FrameView), new UIPropertyMetadata(false));


    public bool Active
    {
      get { return (bool)GetValue(ActiveProperty); }
      set { SetValueDP(ActiveProperty, value); }
    }

    public void Activate(ImageInfo nextphoto, TimeSpan fadetime, TimeSpan movetime, bool accented)
    {
      Active = true;
      SetImage(nextphoto, movetime, accented);

      image.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(1, fadetime));
    }

    public void Deactivate(TimeSpan fadetime)
    {
      Active = false;
      image.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(0, fadetime));
    }

    private void SetImage(ImageInfo nextphoto, TimeSpan movetime, bool accented)
    {
      BitmapImage bmp_img = nextphoto.bitmap;

      image.Stretch = Stretch.Uniform;
      if (bmp_img.Width > bmp_img.Height * 1.2)
        image.Stretch = Stretch.UniformToFill;

      image.Source = bmp_img;
      if (image.ActualHeight > 0 && image.ActualWidth > 0)
      {
        // if image is valid
        if (movetime != TimeSpan.MinValue)
        {
          double cx = image.ActualWidth / 2;
          double cy = image.ActualHeight / 2;
          double cs = 1.0 + 0.1 * _rand.NextDouble();

          if (accented && nextphoto.accent_count != 0)
          {
            double dc = 1;
            dc = bmp_img.PixelHeight / image.ActualHeight;
            var accent = nextphoto.accent;

            cx += accent.X / dc;
            cy += accent.Y / dc;

            cs = 1.05 + 0.4 * _rand.NextDouble();
          }

          ScaleTransform st = new ScaleTransform(1.0, 1.0, cx, cy);
          DoubleAnimation da = new DoubleAnimation(cs, new Duration(movetime));

          st.BeginAnimation(ScaleTransform.ScaleXProperty, da);
          st.BeginAnimation(ScaleTransform.ScaleYProperty, da);

          image.RenderTransform = st;
        }
      }

      nextphoto.ReleaseResources();
    }

    // INotifyPropertyChanged
    public event PropertyChangedEventHandler PropertyChanged;
    private void SetValueDP(DependencyProperty dp, object value, [System.Runtime.CompilerServices.CallerMemberName] string caller_name = null)
    {
      SetValue(dp, value);
      if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs(caller_name));
    }

  }
}
