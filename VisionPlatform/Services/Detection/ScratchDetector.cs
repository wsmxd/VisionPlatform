using OpenCvSharp;
using VisionPlatform.Models;

namespace VisionPlatform.Services.Detection;

/// <summary>
/// 划痕检测：Canny 边缘 + Hough 直线拟合 + 长度过滤。
/// 线状缺陷（划痕、裂纹）在面积类检测下不明显，需用边缘特征提取。
/// </summary>
public sealed class ScratchDetector : IDetector
{
    public DetectorType Type => DetectorType.Scratch;
    public string Name => "划痕检测";

    public List<Defect> Detect(Mat frame, Recipe recipe)
    {
        var defects = new List<Defect>();
        if (!recipe.UseScratch) return defects;

        var roi = RoiHelper.GetRoi(frame, recipe);
        using var region = frame[roi];
        using var gray = new Mat();
        if (region.Channels() == 3)
            Cv2.CvtColor(region, gray, ColorConversionCodes.BGR2GRAY);
        else
            region.CopyTo(gray);

        using var blur = new Mat();
        Cv2.GaussianBlur(gray, blur, new Size(3, 3), 0);

        using var edges = new Mat();
        Cv2.Canny(blur, edges, recipe.ScratchThreshold * 0.5, recipe.ScratchThreshold);

        var lines = Cv2.HoughLinesP(edges, 1, Math.PI / 180, 60, minLineLength: (int)recipe.ScratchMinLength, maxLineGap: 12);
        if (lines is null || lines.Length == 0)
        {
            edges.Dispose();
            return defects;
        }

        // 合并近似共线的短线段
        var segments = new List<(LineSegmentPoint seg, double len)>();
        foreach (var line in lines)
        {
            var dx = line.P1.X - line.P2.X;
            var dy = line.P1.Y - line.P2.Y;
            segments.Add((line, Math.Sqrt(dx * dx + dy * dy)));
        }
        var merged = MergeSegments(segments);

        foreach (var (seg, len) in merged)
        {
            var x = Math.Min(seg.P1.X, seg.P2.X);
            var y = Math.Min(seg.P1.Y, seg.P2.Y);
            var w = Math.Abs(seg.P1.X - seg.P2.X) + 4;
            var h = Math.Abs(seg.P1.Y - seg.P2.Y) + 4;
            defects.Add(new Defect
            {
                Type = Type,
                Name = "划痕",
                BoundingBox = new Rect(x + roi.X, y + roi.Y, w, h),
                Area = len,
                Confidence = Math.Clamp(len / 300.0, 0.3, 1.0),
                Detail = $"长度 {len:F0}px"
            });
        }
        edges.Dispose();
        return defects;
    }

    private static List<(LineSegmentPoint, double)> MergeSegments(List<(LineSegmentPoint seg, double len)> segs)
    {
        var result = new List<(LineSegmentPoint, double)>();
        foreach (var s in segs)
        {
            var merged = false;
            for (int i = 0; i < result.Count; i++)
            {
                var (r, rLen) = result[i];
                // 共线判断：端点距离近且角度接近
                var d1 = Dist(s.seg.P1, r.P1) + Dist(s.seg.P2, r.P2);
                var d2 = Dist(s.seg.P1, r.P2) + Dist(s.seg.P2, r.P1);
                var endDist = Math.Min(d1, d2);
                var lenSum = rLen + s.len;
                if (endDist < 30 && lenSum <= 2.2 * Math.Max(rLen, s.len))
                {
                    var minX = Math.Min(Math.Min(s.seg.P1.X, s.seg.P2.X), Math.Min(r.P1.X, r.P2.X));
                    var maxX = Math.Max(Math.Max(s.seg.P1.X, s.seg.P2.X), Math.Max(r.P1.X, r.P2.X));
                    var minY = Math.Min(Math.Min(s.seg.P1.Y, s.seg.P2.Y), Math.Min(r.P1.Y, r.P2.Y));
                    var maxY = Math.Max(Math.Max(s.seg.P1.Y, s.seg.P2.Y), Math.Max(r.P1.Y, r.P2.Y));
                    var newSeg = new LineSegmentPoint(new OpenCvSharp.Point(minX, minY), new OpenCvSharp.Point(maxX, maxY));
                    var newLen = Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
                    result[i] = (newSeg, newLen);
                    merged = true;
                    break;
                }
            }
            if (!merged) result.Add(s);
        }
        return result;
    }

    private static double Dist(OpenCvSharp.Point a, OpenCvSharp.Point b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
