using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Windows.Interop;
using Emgu.CV;
using Newtonsoft.Json;
using Serilog;

public class LocalImageInfo : ImageInfo
{
  internal string _name;
  internal string _video_name;  // for iPhone accompanying video file
  internal DateTime? _dateTaken;
  internal int _shown = 0;
  internal UInt16 _orientation = 0;

  internal List<string> _messages = new List<string>();

  private List<PointF> _faces = null;
  private bool _processed = false;
  internal double? _latitude = null;
  internal double? _longitude = null;
  internal string _placeName = null;
  private static Random _rand = new Random(DateTime.Now.Millisecond);

  public LocalImageInfo(string nm, string videoname = null)
  {
    _name = nm;
    // _video_name = videoname;
  }

  public RotateFlipType orientation
  {
    get
    {
      var rf = System.Drawing.RotateFlipType.RotateNoneFlipNone;
      switch (_orientation)
      {
        case 1: break;
        case 2: rf = System.Drawing.RotateFlipType.RotateNoneFlipX; break;
        case 3: rf = System.Drawing.RotateFlipType.Rotate180FlipNone; break;
        case 4: rf = System.Drawing.RotateFlipType.RotateNoneFlipY; break;
        case 5: rf = System.Drawing.RotateFlipType.Rotate90FlipX; break;
        case 6: rf = System.Drawing.RotateFlipType.Rotate90FlipNone; break;
        case 7: rf = System.Drawing.RotateFlipType.Rotate270FlipX; break;
        case 8: rf = System.Drawing.RotateFlipType.Rotate270FlipNone; break;
      }

      return rf;
    }
  }


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
      string d = (_dateTaken == null ? "" : _dateTaken.Value.ToString("dd/MM/yyyy"));
      if (!string.IsNullOrEmpty(_placeName))
      {
        if (!string.IsNullOrEmpty(d))
          d += " :: ";
        d += _placeName;
      }
      return d;
    }
  }

  public BitmapImage bitmap
  {
    get
    {
      BitmapImage bmp_img = new BitmapImage(new Uri(_name));

      if (orientation != RotateFlipType.RotateNoneFlipNone || !_processed)
      {
        using (MemoryStream outStream = new MemoryStream())
        {
          BitmapEncoder enc = new BmpBitmapEncoder();
          enc.Frames.Add(BitmapFrame.Create(bmp_img));
          enc.Save(outStream);
          Bitmap bitmap = new Bitmap(outStream);

          bitmap.RotateFlip(orientation);

          bmp_img = Bitmap2BitmapImage(bitmap);

          FindFaces(bitmap);
        }
      }

      return bmp_img;
    }
  }

  public int accent_count
  {
    get
    {
      return _faces != null ? _faces.Count : 0;
    }
  }

  public PointF accent
  {
    get
    {
      PointF pt = new PointF(-1.0F, -1.0F);
      if (_faces != null && _faces.Count != 0)
      {
        int acc = _rand.Next(_faces.Count);
        if (acc >= 0 && acc < _faces.Count)
          pt = _faces[acc];
      }

      return pt;
    }
  }

  private void FindFaces(Bitmap bitmap)
  {
    if (!_processed && bitmap != null)
    {
      double dmult = 3.0;
      int pixel_width = bitmap.Width;
      int pixel_height = bitmap.Height;

      string finfoname = Path.ChangeExtension(_name, "finfo");
      if (File.Exists(finfoname))
      {
        string json = File.ReadAllText(finfoname);
        FinfoData finfo = null;

        try
        {
          finfo = JsonConvert.DeserializeObject<FinfoData>(json);
        }
        catch { }

        if (finfo == null || finfo.Faces == null || finfo.Faces.Length == 0)
        {
          try
          {
            var oldFaces = JsonConvert.DeserializeObject<System.Drawing.Rectangle[]>(json);
            if (oldFaces != null)
              finfo = new FinfoData { Faces = oldFaces };
          }
          catch { }
        }

        if (finfo?.Faces != null && finfo.Faces.Length != 0)
        {
          _faces = new List<PointF>();
          foreach (var f in finfo.Faces)
            _faces.Add(new PointF((float)((f.Right + f.Left) * dmult / 2.0 - pixel_width / 2.0),
                                  (float)((f.Top + f.Bottom) * dmult / 2.0 - pixel_height / 2.0)));
        }

        _placeName = finfo?.PlaceName;
        if (finfo?.Latitude != null) _latitude = finfo.Latitude;
        if (finfo?.Longitude != null) _longitude = finfo.Longitude;

        _processed = true;
        return;
      }

      List<System.Drawing.Rectangle> faces = new List<System.Drawing.Rectangle>();

      System.Drawing.Bitmap b = new System.Drawing.Bitmap((int)(pixel_width / dmult), (int)(pixel_height / dmult), System.Drawing.Imaging.PixelFormat.Format24bppRgb);
      using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage((System.Drawing.Image)b))
      {
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(bitmap, 0, 0, b.Width, b.Height);

        try
        {
          Image<Emgu.CV.Structure.Bgr, byte> cvimg = new Image<Emgu.CV.Structure.Bgr, byte>(b);
          Mat cvmat = new Mat(cvimg.Mat, new System.Drawing.Rectangle(new System.Drawing.Point(0, 0), cvimg.Size));

          long detectionTime;
          string face_detection_file = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), "haarcascade_frontalface_alt2.xml");
          FaceDetection.DetectFace.Detect(cvmat, face_detection_file, faces, out detectionTime);

          FinfoData finfo = new FinfoData
          {
            Faces = faces.ToArray(),
            Latitude = _latitude,
            Longitude = _longitude,
            PlaceName = _placeName
          };

          string json = JsonConvert.SerializeObject(finfo, Formatting.Indented);
          File.WriteAllText(finfoname, json);

          if (faces.Count != 0)
          {
            _faces = new List<PointF>();
            foreach (var f in faces)
              _faces.Add(new PointF((float)((f.Right + f.Left) * dmult / 2.0 - pixel_width / 2.0),
                                    (float)((f.Top + f.Bottom) * dmult / 2.0 - pixel_height / 2.0)));
          }

          if (_latitude != null && _longitude != null && string.IsNullOrEmpty(_placeName))
          {
            double lat = _latitude.Value;
            double lon = _longitude.Value;
            string imgName = _name;

            Task.Run(async () =>
            {
              try
              {
                var result = await GeocodingService.ReverseGeocodeAsync(lat, lon);
                if (result != null && !string.IsNullOrEmpty(result.PlaceName))
                {
                  _placeName = result.PlaceName;

                  string fname = Path.ChangeExtension(imgName, "finfo");
                  string existingJson = File.ReadAllText(fname);
                  var data = JsonConvert.DeserializeObject<FinfoData>(existingJson);
                  if (data != null)
                  {
                    data.PlaceName = result.PlaceName;
                    data.NominatimData = result.FullResponse;
                    File.WriteAllText(fname, JsonConvert.SerializeObject(data, Formatting.Indented));
                  }
                }
              }
              catch (Exception ex)
              {
                Log.Error(ex, "Async geocoding failed for {Image}", imgName);
              }
            });
          }

          _processed = true;
        }
        catch (Exception ex)
        {
          Log.Error(ex, "");
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

