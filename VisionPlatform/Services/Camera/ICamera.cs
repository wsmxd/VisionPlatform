using OpenCvSharp;
using VisionPlatform.Models;

namespace VisionPlatform.Services.Camera;

/// <summary>
/// 相机抽象接口。真实场景可替换为海康/大华等 SDK 实现，
/// 平台仅依赖该接口，业务层无需改动。
/// </summary>
public interface ICamera : IDisposable
{
    string Name { get; }
    CameraSourceType SourceType { get; }
    bool IsOpen { get; }

    /// <summary>打开相机并应用配方参数。</summary>
    bool Open(Recipe recipe);

    /// <summary>非阻塞抓取最新一帧。无新帧时返回 false。</summary>
    bool TryGrab(out Mat frame);

    void Close();
}
