using System.Diagnostics;

namespace SlideshowLouncher
{
  internal class Program
  {
    static void Main(string[] args)
    {
      bool slideshow = false;
      bool devenv = false;
      var proclist = Process.GetProcesses().OrderBy(p => p.ProcessName);
      foreach (var proc in proclist)
      {
        if (proc.ProcessName.StartsWith("PictureSlideshowScreensaver"))
          slideshow = true;
        else if (proc.ProcessName.StartsWith("devenv"))
          devenv = true;
      }

      if (!slideshow && !devenv)
        Process.Start("C:\\Projects\\YetAnotherPictureSlideshow\\PictureSlideshowScreensaver\\bin\\Release\\PictureSlideshowScreensaver.exe");
    }
  }
}