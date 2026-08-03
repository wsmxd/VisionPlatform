using System.IO;
using OpenCvSharp;
using VisionPlatform.Models;

namespace VisionPlatform.Services.Camera;

/// <summary>基于 OpenCV VideoCapture 的 USB/工业相机（UVC）接入。</summary>
public sealed class OpenCvCamera : ICamera
{
    private VideoCapture? _capture;
    private readonly Lock _lock = new();

    public OpenCvCamera(int index)
    {
        Index = index;
        Name = $"OpenCV 相机 {index}";
    }

    public int Index { get; }
    public string Name { get; }
    public CameraSourceType SourceType => CameraSourceType.OpenCvCamera;
    public bool IsOpen
    {
        get { lock (_lock) return _capture?.IsOpened() == true; }
    }

    public bool Open(Recipe recipe)
    {
        lock (_lock)
        {
            CloseCore();
            var cap = new VideoCapture(Index, VideoCaptureAPIs.DSHOW);
            if (!cap.IsOpened())
            {
                cap.Dispose();
                return false;
            }
            _capture = cap;
        }
        try { _capture.Set((VideoCaptureProperties)44, 0); } catch { } // 关闭自动曝光
        try { _capture.Set(VideoCaptureProperties.Exposure, recipe.Exposure); } catch { }
        try { _capture.Set(VideoCaptureProperties.Gain, recipe.Gain); } catch { }
        if (recipe.FrameWidth > 0 && recipe.FrameHeight > 0)
        {
            try { _capture.Set(VideoCaptureProperties.FrameWidth, recipe.FrameWidth); } catch { }
            try { _capture.Set(VideoCaptureProperties.FrameHeight, recipe.FrameHeight); } catch { }
        }
        return true;
    }

    public bool TryGrab(out Mat frame)
    {
        frame = new Mat();
        lock (_lock)
        {
            if (_capture?.IsOpened() != true || !_capture.Read(frame) || frame.Empty())
            {
                frame.Dispose();
                frame = null!;
                return false;
            }
        }
        return true;
    }

    public void Close()
    {
        lock (_lock) CloseCore();
    }

    private void CloseCore()
    {
        _capture?.Dispose();
        _capture = null;
    }

    public void Dispose() => Close();
}
