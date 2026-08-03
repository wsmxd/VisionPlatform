using System.IO;
using OpenCvSharp;
using VisionPlatform.Models;

namespace VisionPlatform.Services.Detection;

/// <summary>
/// 模板匹配：归一化互相关 (TM_CCOEFF_NORMED) 判断工件是否与标准模板一致，
/// 用于缺件、错位、漏加工等整图级缺陷。模板通常取自良品首片。
/// </summary>
public sealed class TemplateDetector : IDetector
{
    private readonly object _lock = new();
    private Mat? _template;
    private string _templatePath = "";

    public DetectorType Type => DetectorType.Template;
    public string Name => "模板匹配";

    /// <summary>缓存模板，配方变化时自动重载。</summary>
    private Mat? GetTemplate(Recipe recipe)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(recipe.TemplatePath) || !File.Exists(recipe.TemplatePath))
                return null;
            if (_template is not null && _templatePath == recipe.TemplatePath)
                return _template;
            _template?.Dispose();
            _template = null;
            var tpl = Cv2.ImRead(recipe.TemplatePath, ImreadModes.Grayscale);
            if (tpl is null || tpl.Empty())
            {
                tpl?.Dispose();
                return null;
            }
            _template = tpl;
            _templatePath = recipe.TemplatePath;
            return _template;
        }
    }

    public List<Defect> Detect(Mat frame, Recipe recipe)
    {
        var defects = new List<Defect>();
        if (!recipe.UseTemplate) return defects;

        var template = GetTemplate(recipe);
        if (template is null)
        {
            defects.Add(new Defect
            {
                Type = Type,
                Name = "模板缺失",
                BoundingBox = new Rect(0, 0, frame.Width, frame.Height),
                Area = 0,
                Confidence = 0,
                Detail = "配方未指定有效模板图像"
            });
            return defects;
        }
        if (template.Width > frame.Width || template.Height > frame.Height)
        {
            defects.Add(new Defect
            {
                Type = Type,
                Name = "模板尺寸过大",
                BoundingBox = new Rect(0, 0, frame.Width, frame.Height),
                Area = 0,
                Confidence = 0,
                Detail = "模板大于当前图像，请重新获取模板"
            });
            return defects;
        }

        using var gray = new Mat();
        if (frame.Channels() == 3)
            Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
        else
            frame.CopyTo(gray);

        using var result = new Mat();
        Cv2.MatchTemplate(gray, template, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

        if (maxVal < recipe.TemplateThreshold)
        {
            defects.Add(new Defect
            {
                Type = Type,
                Name = "模板不匹配",
                BoundingBox = new Rect(0, 0, frame.Width, frame.Height),
                Area = frame.Width * frame.Height,
                Confidence = Math.Clamp(1 - (recipe.TemplateThreshold - maxVal), 0.2, 1),
                Detail = $"最高相似度 {maxVal:F3} < 阈值 {recipe.TemplateThreshold:F2}"
            });
        }
        return defects;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _template?.Dispose();
            _template = null;
        }
    }
}
