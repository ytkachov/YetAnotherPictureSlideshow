using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Windows.Interop;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Serilog;
using Yaps.Core.Abstractions;
using Yaps.Core.Models;

public class LocalImageInfo : ImageInfo
{
  internal string _name;
  internal string _video_name;  // for iPhone accompanying video file
  internal DateTime? _dateTaken;
  internal int _shown = 0;
  internal UInt16 _orientation = 0;

  internal List<string> _messages = new List<string>();

  private List<PointF> _faces = null;
  private volatile bool _processed = false;
  internal double? _latitude = null;
  internal double? _longitude = null;
  internal string _placeName = null;
  private readonly IGeocoder _geocoder;

  public LocalImageInfo(string nm, string videoname = null, IGeocoder geocoder = null)
  {
    _name = nm;
    _video_name = videoname;
    _geocoder = geocoder;
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
      // _placeName may be written by the fire-and-forget geocoding Task in
      // FindFaces; Volatile.Read pairs with the Volatile.Write there to
      // guarantee the UI thread sees the latest value once it lands.
      string place = Volatile.Read(ref _placeName);
      string d = (_dateTaken == null ? "" : _dateTaken.Value.ToString("dd/MM/yyyy"));
      if (!string.IsNullOrEmpty(place))
      {
        if (!string.IsNullOrEmpty(d))
          d += " :: ";
        d += place;
      }
      return d;
    }
  }

  public BitmapImage bitmap
  {
    get
    {
      BitmapImage bmp_img = new BitmapImage(new Uri(_name));

      // Fast path: no rotation needed and face detection already cached.
      if (orientation == RotateFlipType.RotateNoneFlipNone && _processed)
      {
        bmp_img.Freeze();
        return bmp_img;
      }

      using (MemoryStream outStream = new MemoryStream())
      {
        BitmapEncoder enc = new BmpBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp_img));
        enc.Save(outStream);

        // System.Drawing.Bitmap was previously leaked — wrap in using so the
        // unmanaged GDI handle is freed even if FindFaces throws.
        using (Bitmap bitmap = new Bitmap(outStream))
        {
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
      // Snapshot the reference so a concurrent FindFaces assignment cannot
      // shrink the list under our feet.
      var faces = _faces;
      if (faces != null && faces.Count != 0)
      {
        int acc = Random.Shared.Next(faces.Count);
        if (acc >= 0 && acc < faces.Count)
          pt = faces[acc];
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
        // ReadFromFile transparently handles legacy Rectangle[] files; we
        // no longer need the manual fallback that lived here.
        FinfoData finfo = FinfoData.ReadFromFile(finfoname);

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
          using Mat cvmat = BitmapConverter.ToMat(b);

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

          FinfoData.WriteToFile(finfoname, finfo);

          if (faces.Count != 0)
          {
            _faces = new List<PointF>();
            foreach (var f in faces)
              _faces.Add(new PointF((float)((f.Right + f.Left) * dmult / 2.0 - pixel_width / 2.0),
                                    (float)((f.Top + f.Bottom) * dmult / 2.0 - pixel_height / 2.0)));
          }

          if (_latitude != null && _longitude != null && string.IsNullOrEmpty(_placeName) && _geocoder != null)
          {
            double lat = _latitude.Value;
            double lon = _longitude.Value;
            string imgName = _name;
            var geocoder = _geocoder;

            Task.Run(async () =>
            {
              try
              {
                var result = await geocoder.ReverseGeocodeAsync(lat, lon);

                string fname = Path.ChangeExtension(imgName, "finfo");
                var data = FinfoData.ReadFromFile(fname);
                if (data != null)
                {
                  data.GeocodingAttempted = true;
                  if (result != null && !string.IsNullOrEmpty(result.PlaceName))
                  {
                    // Volatile.Write pairs with Volatile.Read in the description
                    // getter so the UI thread observes the new place name
                    // without needing a lock.
                    Volatile.Write(ref _placeName, result.PlaceName);
                    data.PlaceName = result.PlaceName;
                    data.NominatimData = result.FullResponse;
                  }
                  FinfoData.WriteToFile(fname, data);
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

    // Freeze for cross-thread safety: animation/property updates may touch
    // the BitmapImage from the dispatcher while it's also referenced by
    // background tasks. A frozen Freezable is allowed on any thread.
    bitmapImage.Freeze();
    return bitmapImage;
  }

}

