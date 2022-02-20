using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PictureSlideshowScreensaver.Models
{
  class Settings
  {
    public string _path = null;
    public double _updateInterval = 5; // seconds
    public int _fadeSpeed = 200;       // milliseconds
    public int _startOffset = 0;
    public bool _writeStat = false;
    public string _writeStatPath;
    public bool _dependOnBattery = false;
    public bool _workAtNight = true;
    public bool _noImageFading = false;
    public bool _noImageScaling = false;
    public bool _noImageAccents = false;
    public bool _noNightImageFading = true;
    public bool _noNightImageScaling = true;
    public bool _noNightImageAccents = true;

    enum perfoptions
    {
      work_at_night = 0x0001,
      no_image_fading = 0x0002,
      no_image_scaling = 0x0004,
      no_image_accents = 0x0008,
      no_night_image_fading = 0x0020,
      no_night_image_scaling = 0x0040,
      no_night_image_accents = 0x0080
    }

    public Settings()
    {
      RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\PictureSlideshowScreensaver");
      if (key != null)
      {
        _path = (string)key.GetValue("ImageFolder");
        _updateInterval = double.Parse((string)key.GetValue("Interval") ?? "10.0");
        _fadeSpeed = int.Parse((string)key.GetValue("FadeTime") ?? "1000");
        _writeStat = int.Parse((string)key.GetValue("WriteStat") ?? "0") == 1;
        _writeStatPath = (string)key.GetValue("WriteStatFolder");
        _dependOnBattery = int.Parse((string)key.GetValue("DependOnBattery") ?? "0") == 1;

        int dflt = (int)(perfoptions.work_at_night | perfoptions.no_night_image_accents | perfoptions.no_night_image_fading | perfoptions.no_night_image_scaling);
        int po = (int?)key.GetValue("PerformanceOptions") ?? dflt;
        _workAtNight = (po & (int)perfoptions.work_at_night) != 0;
        _noImageFading = (po & (int)perfoptions.no_image_fading) != 0;
        _noImageScaling = (po & (int)perfoptions.no_image_scaling) != 0;
        _noImageAccents = (po & (int)perfoptions.no_image_accents) != 0;
        _noNightImageFading = (po & (int)perfoptions.no_night_image_fading) != 0;
        _noNightImageScaling = (po & (int)perfoptions.no_night_image_scaling) != 0;
        _noNightImageAccents = (po & (int)perfoptions.no_night_image_accents) != 0;
      }
    }
  }
}
