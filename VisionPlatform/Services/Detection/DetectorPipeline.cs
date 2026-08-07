using System.Diagnostics;
using OpenCvSharp;
using VisionPlatform.Models;

namespace VisionPlatform.Services.Detection;

/// <summary>
/// 检测流水线：按配方顺序执行各检测器，聚合缺陷并生成检测结果。
/// </summary>
public sealed class DetectorPipeline
{
    private readonly List<IDetector> _detectors =
    [
        new BlobDetector(),
        new ScratchDetector(),
        new TemplateDetector(),
        new BrightnessDetector()
    ];

    public InspectionResult Inspect(Mat frame, Recipe recipe, string serialNumber)
    {
        var sw = Stopwatch.StartNew();
        var defects = new List<Defect>();

        foreach (var detector in _detectors)
        {
            try
            {
                defects.AddRange(detector.Detect(frame, recipe));
            }
            catch (Exception ex)
            {
                defects.Add(new Defect
                {
                    Type = detector.Type,
                    Name = $"{detector.Name}异常",
                    BoundingBox = new Rect(0, 0, frame.Width, frame.Height),
                    Area = 0,
                    Confidence = 0,
                    Detail = ex.Message
                });
            }
        }

        sw.Stop();
        // 划痕条数上限：NG 条件之一
        if (recipe.UseScratch)
        {
            var scratchCount = defects.Count(d => d.Type == DetectorType.Scratch);
            if (scratchCount > recipe.ScratchMaxCount)
            {
                defects.Add(new Defect
                {
                    Type = DetectorType.Scratch,
                    Name = "划痕数量超限",
                    BoundingBox = new Rect(0, 0, frame.Width, frame.Height),
                    Area = scratchCount,
                    Confidence = 0.8,
                    Detail = $"划痕 {scratchCount} 条 > 上限 {recipe.ScratchMaxCount:F0}"
                });
            }
        }

        return new InspectionResult
        {
            Timestamp = DateTime.Now,
            ProductName = recipe.Name,
            RecipeName = recipe.Name,
            SerialNumber = serialNumber,
            IsOk = defects.Count == 0,
            Defects = defects,
            ElapsedMs = sw.Elapsed.TotalMilliseconds,
            Width = frame.Width,
            Height = frame.Height
        };
    }

    public void Dispose()
    {
        foreach (var d in _detectors)
            (d as IDisposable)?.Dispose();
    }
}
