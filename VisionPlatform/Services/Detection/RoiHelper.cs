using OpenCvSharp;
using VisionPlatform.Models;

namespace VisionPlatform.Services.Detection;

/// <summary>ROI 计算辅助：配方指定检测区域，0 表示全图。</summary>
public static class RoiHelper
{
    public static Rect GetRoi(Mat frame, Recipe recipe)
    {
        if (recipe.RoiW <= 0 || recipe.RoiH <= 0)
            return new Rect(0, 0, frame.Width, frame.Height);
        var x = Math.Clamp((int)recipe.RoiX, 0, frame.Width - 1);
        var y = Math.Clamp((int)recipe.RoiY, 0, frame.Height - 1);
        var w = Math.Clamp((int)recipe.RoiW, 1, frame.Width - x);
        var h = Math.Clamp((int)recipe.RoiH, 1, frame.Height - y);
        return new Rect(x, y, w, h);
    }
}
