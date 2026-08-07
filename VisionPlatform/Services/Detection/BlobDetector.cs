using OpenCvSharp;
using VisionPlatform.Models;

namespace VisionPlatform.Services.Detection;

/// <summary>
/// 斑点检测：二值化 + 形态学去噪 + 连通域分析，
/// 用于脏污、异物、缺损等面积型缺陷。
/// </summary>
public sealed class BlobDetector : IDetector
{
    public DetectorType Type => DetectorType.Blob;
    public string Name => "斑点检测";

    public List<Defect> Detect(Mat frame, Recipe recipe)
    {
        var defects = new List<Defect>();
        if (!recipe.UseBlob) return defects;

        var roi = RoiHelper.GetRoi(frame, recipe);
        using var region = frame[roi];
        using var gray = new Mat();
        if (region.Channels() == 3)
            Cv2.CvtColor(region, gray, ColorConversionCodes.BGR2GRAY);
        else
            region.CopyTo(gray);

        using var blur = new Mat();
        Cv2.GaussianBlur(gray, blur, new Size(5, 5), 0);

        using var bin = new Mat();
        Cv2.Threshold(blur, bin, recipe.BlobThreshold, 255, ThresholdTypes.BinaryInv);

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
        using var morph = new Mat();
        Cv2.MorphologyEx(bin, morph, MorphTypes.Open, kernel);
        Cv2.MorphologyEx(morph, morph, MorphTypes.Close, kernel);

        Cv2.FindContours(morph, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            if (area < recipe.BlobMinArea || area > recipe.BlobMaxArea) continue;

            var bbox = Cv2.BoundingRect(contour);
            // 置信度：缺陷区域平均灰度相对背景的偏差
            using var mask = new Mat(gray.Size(), MatType.CV_8UC1, Scalar.All(0));
            Cv2.DrawContours(mask, [contour], -1, new Scalar(255), -1);
            var mean = Cv2.Mean(gray, mask).Val0;
            var confidence = Math.Clamp((255 - mean) / 255.0 * 1.2, 0, 1);

            defects.Add(new Defect
            {
                Type = Type,
                Name = area > 2000 ? "脏污/异物" : "污点",
                BoundingBox = new Rect(bbox.X + roi.X, bbox.Y + roi.Y, bbox.Width, bbox.Height),
                Area = area,
                Confidence = confidence,
                Detail = $"面积 {area:F0}px², 平均灰度 {mean:F0}"
            });
        }
        return defects;
    }
}
