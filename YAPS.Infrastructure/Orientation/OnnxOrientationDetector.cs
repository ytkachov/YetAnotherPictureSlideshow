using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using CvSize = OpenCvSharp.Size;

namespace Yaps.Infrastructure.Orientation;

/// <summary>
/// ONNX Runtime CPU implementation of <see cref="IOrientationDetector"/>.
/// Wraps the exported `check_orientation` ResNeXt-50 (the export bakes a
/// final <c>Softmax</c>, so the network output IS the probability vector).
///
/// The <see cref="InferenceSession"/> is reused across calls — Run is
/// thread-safe and re-creating the session per photo would re-parse the
/// 90 MB model on every call.
/// </summary>
public sealed class OnnxOrientationDetector : IOrientationDetector, IDisposable
{
    // Standard ImageNet preprocessing, matching the Python tool exactly.
    private static readonly float[] _mean = { 0.485f, 0.456f, 0.406f };
    private static readonly float[] _std  = { 0.229f, 0.224f, 0.225f };
    private const int InputSize = 224;

    // Model class index -> EXIF orientation code. The Python tool calibrates
    // this per machine; on this model the mapping is identity (verified by
    // --self-test on 2026-05). If a future model uses a different rotation
    // convention, plumb an explicit calibration here.
    //   factor 0 (upright)       -> code 1
    //   factor 1 (stored CCW 90) -> code 6  (viewer rotates 90 CW)
    //   factor 2 (180)           -> code 3
    //   factor 3 (stored CCW 270)-> code 8  (viewer rotates 90 CCW)
    private static readonly int[] _factorToCode = { 1, 6, 3, 8 };

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private bool _disposed;

    public OnnxOrientationDetector(string onnxModelPath)
    {
        if (string.IsNullOrEmpty(onnxModelPath))
            throw new ArgumentException("Model path is required", nameof(onnxModelPath));
        if (!File.Exists(onnxModelPath))
            throw new FileNotFoundException("Orientation ONNX model not found", onnxModelPath);

        _session = new InferenceSession(onnxModelPath);
        // The exporter named it "input"; cache it instead of hard-coding so a
        // re-export with a different name still works.
        _inputName = _session.InputMetadata.Keys.GetEnumerator() is { } it && it.MoveNext()
            ? it.Current
            : "input";
    }

    public OrientationDetection Detect(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var input = Preprocess(bitmap);
        var tensor = new DenseTensor<float>(input, new[] { 1, 3, InputSize, InputSize });
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };

        using var results = _session.Run(inputs);
        ReadOnlySpan<float> probs;
        using (var first = results.GetEnumerator())
        {
            if (!first.MoveNext())
                throw new InvalidOperationException("Orientation model returned no output");
            probs = first.Current.AsTensor<float>().ToArray();
        }

        int argmax = 0;
        float best = probs[0];
        for (int i = 1; i < probs.Length; i++)
        {
            if (probs[i] > best)
            {
                best = probs[i];
                argmax = i;
            }
        }

        int code = argmax >= 0 && argmax < _factorToCode.Length ? _factorToCode[argmax] : 1;
        return new OrientationDetection(code, best);
    }

    // Resize → BGR2RGB → float[0,1] → ImageNet-normalize → NCHW. Done with
    // OpenCV (already in the project) instead of GDI to match the Python
    // (albumentations) preprocessing as closely as the C# stack permits.
    private static float[] Preprocess(Bitmap bitmap)
    {
        using var bgr = BitmapConverter.ToMat(bitmap);
        using var resized = new Mat();
        Cv2.Resize(bgr, resized, new CvSize(InputSize, InputSize));
        using var rgb = new Mat();
        Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);
        using var f = new Mat();
        rgb.ConvertTo(f, MatType.CV_32FC3, 1.0 / 255.0);

        const int hw = InputSize * InputSize;
        var data = new float[3 * hw];
        var indexer = f.GetGenericIndexer<Vec3f>();
        for (int y = 0; y < InputSize; y++)
        {
            int row = y * InputSize;
            for (int x = 0; x < InputSize; x++)
            {
                Vec3f p = indexer[y, x];
                int idx = row + x;
                data[0 * hw + idx] = (p.Item0 - _mean[0]) / _std[0];
                data[1 * hw + idx] = (p.Item1 - _mean[1]) / _std[1];
                data[2 * hw + idx] = (p.Item2 - _mean[2]) / _std[2];
            }
        }
        return data;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Dispose();
        GC.SuppressFinalize(this);
    }
}
