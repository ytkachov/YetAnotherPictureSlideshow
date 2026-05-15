using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using OpenCvSharp;
using CvRect = OpenCvSharp.Rect;
using DrawingRectangle = System.Drawing.Rectangle;

namespace FaceDetection
{
  public static class DetectFace
  {
    public static void Detect(
      Mat image, String faceFileName,
      List<DrawingRectangle> faces,
      out long detectionTime)
    {
      Stopwatch watch;

      using (CascadeClassifier face = new CascadeClassifier(faceFileName))
      {
        watch = Stopwatch.StartNew();
        using (Mat gray = new Mat())
        {
          Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
          Cv2.EqualizeHist(gray, gray);

          CvRect[] facesDetected = face.DetectMultiScale(gray, 1.1, 10, (HaarDetectionTypes)0, new OpenCvSharp.Size(20, 20));

          foreach (var r in facesDetected)
            faces.Add(new DrawingRectangle(r.X, r.Y, r.Width, r.Height));
        }
        watch.Stop();
      }

      detectionTime = watch.ElapsedMilliseconds;
    }
  }
}
