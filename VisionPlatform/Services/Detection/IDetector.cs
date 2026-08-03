using OpenCvSharp;
using VisionPlatform.Models;

namespace VisionPlatform.Services.Detection;

/// <summary>检测器抽象：输入一帧图像与配方参数，输出缺陷列表。</summary>
public interface IDetector
{
    DetectorType Type { get; }
    string Name { get; }
    List<Defect> Detect(Mat frame, Recipe recipe);
}
