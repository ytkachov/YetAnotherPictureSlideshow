using System.Windows.Media.Imaging;
using System.Drawing;

interface ImageInfo
{
  BitmapImage bitmap { get; }
  string description { get; }

  int accent_count { get; }
  PointF accent { get; }
  PointF get_accent(int idx);
}


interface ImagesProvider
{
  void init(string [] parameters);
  ImageInfo GetNext();
  void WriteStat(string write_stat_path);

}

