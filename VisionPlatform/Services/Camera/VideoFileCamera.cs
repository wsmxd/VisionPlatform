using System.IO;
using OpenCvSharp;
using VisionPlatform.Models;

namespace VisionPlatform.Services.Camera;

/// <summary>视频文件相机：循环播放本地视频，模拟产线持续供料。</summary>
public sealed class VideoFileCamera : ICamera
{
    private VideoCapture? _capture;
    private readonly Lock _lock = new();
    private int _width, _height;

    public VideoFileCamera(string filePath)
    {
        FilePath = filePath;
        Name = $"视频文件 {Path.GetFileName(filePath)}";
    }

    public string FilePath { get; }
    public string Name { get; }
    public CameraSourceType SourceType => CameraSourceType.VideoFile;
    public bool IsOpen
    {
        get { lock (_lock) return _capture?.IsOpened() == true; }
    }

    public bool Open(Recipe recipe)
    {
        lock (_lock)
        {
            CloseCore();
            var cap = new VideoCapture(FilePath);
            if (!cap.IsOpened())
            {
                cap.Dispose();
                return false;
            }
            _capture = cap;
            _width = cap.FrameWidth;
            _height = cap.FrameHeight;
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
            var pos = _capture.PosFrames;
            if (pos >= _capture.FrameCount - 1)
            {
                _capture.Set(VideoCaptureProperties.PosFrames, 0); // 循环播放
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
