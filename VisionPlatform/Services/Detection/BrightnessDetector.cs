using OpenCvSharp;
using VisionPlatform.Models;

namespace VisionPlatform.Services.Detection;

/// <summary>
/// 亮度检测：统计整体平均灰度，判定过暗/过亮/光照异常。
/// </summary>
public sealed class BrightnessDetector : IDetector
{
    public DetectorType Type => DetectorType.Brightness;
    public string Name => "亮度检测";

    public List<Defect> Detect(Mat frame, Recipe recipe)
    {
        var defects = new List<Defect>();
        if (!recipe.UseBrightness) return defects;

        var roi = RoiHelper.GetRoi(frame, recipe);
        using var region = frame[roi];
        using var gray = new Mat();
        if (region.Channels() == 3)
            Cv2.CvtColor(region, gray, ColorConversionCodes.BGR2GRAY);
        else
            region.CopyTo(gray);

        var mean = Cv2.Mean(gray).Val0;
        if (mean < recipe.BrightnessMin)
        {
            defects.Add(new Defect
            {
                Type = Type,
                Name = "图像过暗",
                BoundingBox = new Rect(0, 0, frame.Width, frame.Height),
                Area = frame.Width * frame.Height,
                Confidence = Math.Clamp((recipe.BrightnessMin - mean) / recipe.BrightnessMin, 0.3, 1),
                Detail = $"平均灰度 {mean:F0} < {recipe.BrightnessMin:F0}"
            });
        }
        else if (mean > recipe.BrightnessMax)
        {
            defects.Add(new Defect
            {
                Type = Type,
                Name = "图像过亮",
                BoundingBox = new Rect(0, 0, frame.Width, frame.Height),
                Area = frame.Width * frame.Height,
                Confidence = Math.Clamp((mean - recipe.BrightnessMax) / (255 - recipe.BrightnessMax), 0.3, 1),
                Detail = $"平均灰度 {mean:F0} > {recipe.BrightnessMax:F0}"
            });
        }
        return defects;
    }
}
