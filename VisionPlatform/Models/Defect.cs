using OpenCvSharp;

namespace VisionPlatform.Models;

/// <summary>单个缺陷实体（像素坐标系）。</summary>
public class Defect
{
    public required DetectorType Type { get; init; }
    public required string Name { get; init; }
    public required Rect BoundingBox { get; init; }
    public double Area { get; init; }
    public double Confidence { get; init; }
    public string Detail { get; init; } = "";
}
