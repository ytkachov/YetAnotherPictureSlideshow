using System.Windows.Media.Imaging;
using System.Drawing;

public interface ImageInfo
{
  BitmapImage bitmap { get; }
  bool has_accompanying_video { get; }
  string video_name { get; }
  string description { get; }

  int accent_count { get; }
  RotateFlipType orientation { get; }
  PointF accent { get; }
}


public interface ImagesProvider
{
  void init(string [] parameters);
  ImageInfo GetNext();
  void WriteStat(string write_stat_path);

}

