using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using ExifLib;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Windows.Interop;
using Emgu.CV;
using Newtonsoft.Json;

class LocalImageInfo : ImageInfo
{
  public LocalImageInfo(string nm, string videoname = null) { _name = nm; _video_name = videoname; }

  internal string _name;
  internal string _video_name;  // for iPhone accompanying video file
  internal DateTime? _dateTaken;
  internal int _shown = 0;
  internal UInt16 _orientation = 0;

  internal List<string> _messages = new List<string>();

  private List<System.Drawing.Rectangle> _faces = null;
  private bool _processed = false;
  private double _dmult;
  private int _pixel_width, _pixel_height;
  private System.Drawing.Bitmap _bitmap;
  private static Random _rand = new Random(DateTime.Now.Millisecond);

  public bool has_accompanying_video
  {
    get
    {
      return _video_name != null && _name != null;
    }
  }
  public string video_name
  {
    get
    {
      return _video_name;
    }
  }

  public string description
  {
    get
    {
      // return _name + " :: " + (_dateTaken == null ? "" : _dateTaken.Value.ToString("dd/MM/yyyy"));
      return _dateTaken == null ? "" : _dateTaken.Value.ToString("dd/MM/yyyy");
    }
  }

  public BitmapImage bitmap
  {
    get
    {
      if (_bitmap != null)
        return Bitmap2BitmapImage(_bitmap);

      BitmapImage bmp_img = new BitmapImage(new Uri(_name));
      using (MemoryStream outStream = new MemoryStream())
      {
        BitmapEncoder enc = new BmpBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp_img));
        enc.Save(outStream);
        _bitmap = new System.Drawing.Bitmap(outStream);

        System.Drawing.RotateFlipType rf = System.Drawing.RotateFlipType.RotateNoneFlipNone;
        switch (_orientation)
        {
          case 1:
            break;

          case 2:
            rf = System.Drawing.RotateFlipType.RotateNoneFlipX;
            break;

          case 3:
            rf = System.Drawing.RotateFlipType.Rotate180FlipNone;
            break;
          case 4:
            rf = System.Drawing.RotateFlipType.RotateNoneFlipY;
            break;
          case 5:
            rf = System.Drawing.RotateFlipType.Rotate90FlipX;
            break;
          case 6:
            rf = System.Drawing.RotateFlipType.Rotate90FlipNone;
            break;
          case 7:
            rf = System.Drawing.RotateFlipType.Rotate270FlipX;
            break;
          case 8:
            rf = System.Drawing.RotateFlipType.Rotate270FlipNone;
            break;
        }

        if (rf != System.Drawing.RotateFlipType.RotateNoneFlipNone)
        {
          _bitmap.RotateFlip(rf);
          bmp_img = Bitmap2BitmapImage(_bitmap);
        }
      }

      _pixel_width = bmp_img.PixelWidth;
      _pixel_height = bmp_img.PixelHeight;


      return bmp_img;
    }
  }

  public int accent_count
  {
    get
    {
      FindFaces();
      return _faces != null ? _faces.Count : 0;
    }
  }

  public PointF accent
  {
    get
    {
      FindFaces();
      return get_accent(_rand.Next(_faces.Count));
    }
  }

  public PointF get_accent(int acc)
  {
    float fx = -1.0F, fy = -1.0F;
    if (_faces != null && _faces.Count != 0 && acc >= 0 && acc < _faces.Count)
    {
      fx = (float)((_faces[acc].Right + _faces[acc].Left) * _dmult / 2.0 - _pixel_width / 2.0);
      fy = (float)((_faces[acc].Top + _faces[acc].Bottom) * _dmult / 2.0 - _pixel_height / 2.0);
    }

    return new PointF(fx, fy);
  }

  public void ReleaseResources()
  {
    if (_bitmap != null)
    {
      _bitmap.Dispose();
      _bitmap = null;
    }
  }

  private void FindFaces()
  {

    if (!_processed)
    {
      // Может быть, лица уже есть в файле?
      string finfoname = Path.ChangeExtension(_name, "finfo");
      if (File.Exists(finfoname))
      {
        string json = File.ReadAllText(finfoname);
        var rfaces = JsonConvert.DeserializeObject<System.Drawing.Rectangle[]>(json);
        if (rfaces != null && rfaces.Length != 0)
          _faces = new List<Rectangle>(rfaces);

        _processed = true;
        return;
      }

      _dmult = 3.0;
      List<System.Drawing.Rectangle> faces = new List<System.Drawing.Rectangle>();

      System.Drawing.Bitmap b = new System.Drawing.Bitmap((int)(_pixel_width / _dmult), (int)(_pixel_height / _dmult), System.Drawing.Imaging.PixelFormat.Format24bppRgb);
      using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage((System.Drawing.Image)b))
      {
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(_bitmap, 0, 0, b.Width, b.Height);

        try
        {
          Image<Emgu.CV.Structure.Bgr, byte> cvimg = new Image<Emgu.CV.Structure.Bgr, byte>(b);
          Mat cvmat = new Mat(cvimg.Mat, new System.Drawing.Rectangle(new System.Drawing.Point(0, 0), cvimg.Size));

          long detectionTime;
          FaceDetection.DetectFace.Detect(cvmat, "haarcascade_frontalface_alt2.xml", faces, out detectionTime);

          string json = JsonConvert.SerializeObject(faces.ToArray(), Formatting.Indented);
          File.WriteAllText(finfoname, json);

          if (faces.Count != 0)
            _faces = faces;

          _processed = true;
        }
        catch (Exception e)
        {
          string err = e.Message;
        }
      }
    }
  }

  private BitmapImage Bitmap2BitmapImage(System.Drawing.Bitmap bitmap)
  {
    BitmapImage bitmapImage = new BitmapImage();
    using (MemoryStream outStream = new MemoryStream())
    {
      bitmap.Save(outStream, System.Drawing.Imaging.ImageFormat.Bmp);
      outStream.Position = 0;
      bitmapImage.BeginInit();
      bitmapImage.StreamSource = outStream;
      bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
      bitmapImage.EndInit();
    }

    return (BitmapImage)bitmapImage;
  }

}

