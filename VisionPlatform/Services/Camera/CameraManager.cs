using System.Collections.ObjectModel;
using System.IO;
using OpenCvSharp;
using VisionPlatform.Models;

namespace VisionPlatform.Services.Camera;

/// <summary>相机源描述（用于下拉选择）。</summary>
public class CameraItem
{
    public required string Name { get; init; }
    public required CameraSourceType SourceType { get; init; }
    public int Index { get; init; } = -1;
    public string? FilePath { get; init; }

    public override string ToString() => Name;
}

/// <summary>相机管理器：枚举可用相机源、创建并持有当前相机。</summary>
public sealed class CameraManager : IDisposable
{
    private readonly object _lock = new();
    public ICamera? CurrentCamera { get; private set; }
    public CameraItem? CurrentItem { get; private set; }

    public ObservableCollection<CameraItem> AvailableCameras { get; } = [];

    public event Action? CameraOpened;
    public event Action? CameraClosed;

    public CameraManager()
    {
        AvailableCameras.Add(new CameraItem { Name = "模拟相机 (演示)", SourceType = CameraSourceType.Simulated });
        AvailableCameras.Add(new CameraItem { Name = "视频文件 (未选择)", SourceType = CameraSourceType.VideoFile });
    }

    /// <summary>扫描本机可用的 OpenCV/USB 相机（异步，避免阻塞 UI）。</summary>
    public async Task ScanOpenCvCamerasAsync()
    {
        AvailableCameras.Where(c => c.SourceType == CameraSourceType.OpenCvCamera).ToList()
                        .ForEach(c => AvailableCameras.Remove(c));
        var found = new List<int>();
        await Task.Run(() =>
        {
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    using var cap = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
                    if (cap.IsOpened() && cap.FrameWidth > 0)
                        found.Add(i);
                }
                catch { }
            }
        });
        foreach (var idx in found)
            AvailableCameras.Add(new CameraItem { Name = $"OpenCV 相机 {idx}", SourceType = CameraSourceType.OpenCvCamera, Index = idx });
    }

    public void SetVideoFile(string path)
    {
        var item = new CameraItem { Name = $"视频文件 {Path.GetFileName(path)}", SourceType = CameraSourceType.VideoFile, FilePath = path };
        var old = AvailableCameras.FirstOrDefault(c => c.SourceType == CameraSourceType.VideoFile);
        if (old is not null) AvailableCameras.Remove(old);
        AvailableCameras.Insert(0, item);
    }

    public bool Open(CameraItem item, Recipe recipe)
    {
        lock (_lock)
        {
            CloseCore();
            ICamera? cam = item.SourceType switch
            {
                CameraSourceType.OpenCvCamera => new OpenCvCamera(item.Index),
                CameraSourceType.VideoFile => string.IsNullOrEmpty(item.FilePath)
                    ? null
                    : new VideoFileCamera(item.FilePath),
                _ => new SimulatedCamera()
            };
            if (cam is null) return false;
            if (!cam.Open(recipe))
            {
                cam.Dispose();
                return false;
            }
            CurrentCamera = cam;
            CurrentItem = item;
        }
        CameraOpened?.Invoke();
        return true;
    }

    public void Close()
    {
        lock (_lock) CloseCore();
        CameraClosed?.Invoke();
    }

    private void CloseCore()
    {
        CurrentCamera?.Dispose();
        CurrentCamera = null;
        CurrentItem = null;
    }

    public void Dispose() => Close();
}
