using System.Diagnostics;
using OpenCvSharp;
using VisionPlatform.Models;

namespace VisionPlatform.Services.Camera;

/// <summary>
/// 模拟相机：程序化绘制"金属工件"图像并按概率注入真实缺陷
/// （脏污斑点 / 划痕 / 缺孔 / 异物 / 亮度异常），无硬件即可完整演示检测流水线。
/// </summary>
public sealed class SimulatedCamera : ICamera
{
    public const int DefaultWidth = 960;
    public const int DefaultHeight = 720;

    private readonly Random _rnd = new();
    private readonly Stopwatch _timer = new();
    private bool _open;
    private double _intervalMs = 500;
    private int _width = DefaultWidth, _height = DefaultHeight;

    // 工件几何
    private Rect _plate = new(180, 120, 600, 480);
    private readonly OpenCvSharp.Point[] _holes =
    [
        new(320, 260), new(640, 260), new(320, 460), new(640, 460)
    ];
    private const int HoleRadius = 24;

    public string Name => "模拟相机 (演示)";
    public CameraSourceType SourceType => CameraSourceType.Simulated;
    public bool IsOpen => _open;

    /// <summary>当前帧是否注入了缺陷（用于和检测结果对照，验证算法有效性）。</summary>
    public bool LastFrameHasDefect { get; private set; }
    public string LastFrameDefectInfo { get; private set; } = "";

    public bool Open(Recipe recipe)
    {
        _intervalMs = Math.Max(80, recipe.TriggerIntervalMs);
        if (recipe.FrameWidth > 0 && recipe.FrameHeight > 0)
        {
            _width = recipe.FrameWidth;
            _height = recipe.FrameHeight;
        }
        _open = true;
        _timer.Restart();
        return true;
    }

    public bool TryGrab(out Mat frame)
    {
        frame = new Mat();
        if (!_open) return false;
        lock (_timer)
        {
            if (_timer.ElapsedMilliseconds < _intervalMs) return false;
            _timer.Restart();
        }
        RenderFrame(frame);
        return true;
    }

    private void RenderFrame(Mat dst)
    {
        var rnd = _rnd;
        // 背景
        var img = new Mat(_height, _width, MatType.CV_8UC3, new Scalar(52, 50, 46));
        LastFrameHasDefect = false;
        LastFrameDefectInfo = "";
        var defectType = rnd.Next(5); // 0 无缺陷 / 1-4 各类缺陷

        // 工件本体：浅灰金属板 + 倒角
        Cv2.Rectangle(img, new Rect(_plate.X + 6, _plate.Y + 6, _plate.Width, _plate.Height), new Scalar(152, 150, 145), -1);
        Cv2.Rectangle(img, new Rect(_plate.X, _plate.Y, _plate.Width, _plate.Height), new Scalar(112, 110, 106), 4);

        // 4 个定位标记（亮色，不影响斑点检测；缺件时模板匹配会报缺孔）
        for (int i = 0; i < _holes.Length; i++)
        {
            var p = _holes[i];
            var miss = defectType == 3 && i == rnd.Next(_holes.Length);
            if (miss)
            {
                // 缺孔：标记缺失并留下深色印记（斑点/模板检测均能捕获）
                Cv2.Circle(img, p, HoleRadius + 3, new Scalar(52, 50, 46), -1);
                Cv2.Circle(img, p, HoleRadius - 6, new Scalar(90, 88, 84), -1);
                LastFrameHasDefect = true;
                LastFrameDefectInfo = "缺孔";
            }
            else
            {
                Cv2.Circle(img, p, HoleRadius, new Scalar(228, 226, 220), -1);
                Cv2.Circle(img, p, HoleRadius, new Scalar(200, 198, 193), 2);
            }
        }

        // 边缘压痕细节（模拟冲压纹路）
        for (int i = 0; i < 6; i++)
        {
            var y = _plate.Y + 40 + i * 72;
            Cv2.Line(img, new OpenCvSharp.Point(_plate.X + 20, y), new OpenCvSharp.Point(_plate.X + _plate.Width - 20, y), new Scalar(138, 136, 131), 1);
        }

        switch (defectType)
        {
            case 1: // 脏污：深色斑点，2~4 个
            {
                var n = rnd.Next(2, 5);
                for (int i = 0; i < n; i++)
                {
                    var x = rnd.Next((int)_plate.X + 40, (int)(_plate.X + _plate.Width - 40));
                    var y = rnd.Next((int)_plate.Y + 40, (int)(_plate.Y + _plate.Height - 40));
                    var r = rnd.Next(12, 34);
                    Cv2.Circle(img, new OpenCvSharp.Point(x, y), r, new Scalar(rnd.Next(20, 60), rnd.Next(20, 60), rnd.Next(20, 60)), -1);
                    Cv2.GaussianBlur(img, img, new Size(9, 9), 0);
                }
                LastFrameHasDefect = true;
                LastFrameDefectInfo = $"脏污×{n}";
                break;
            }
            case 2: // 划痕：细长亮线
            {
                var n = rnd.Next(1, 4);
                for (int i = 0; i < n; i++)
                {
                    var x1 = rnd.Next((int)_plate.X + 60, (int)(_plate.X + _plate.Width - 60));
                    var y1 = rnd.Next((int)_plate.Y + 60, (int)(_plate.Y + _plate.Height - 60));
                    var len = rnd.Next(80, 260);
                    var angle = rnd.NextDouble() * Math.PI;
                    var x2 = x1 + (int)(len * Math.Cos(angle));
                    var y2 = y1 + (int)(len * Math.Sin(angle));
                    Cv2.Line(img, new OpenCvSharp.Point(x1, y1), new OpenCvSharp.Point(x2, y2), new Scalar(215, 213, 208), rnd.Next(1, 3));
                }
                LastFrameHasDefect = true;
                LastFrameDefectInfo = $"划痕×{n}";
                break;
            }
            case 3: // 缺孔（已在孔绘制处注入）
                break;
            case 4: // 亮度异常：整体变暗/变亮
            {
                var dark = rnd.Next(2) == 0;
                var delta = dark ? -rnd.Next(90, 131) : rnd.Next(90, 131);
                img.ConvertTo(img, -1, 1.0, delta);
                LastFrameHasDefect = true;
                LastFrameDefectInfo = dark ? "整体过暗" : "整体过亮";
                break;
            }
        }

        // 传感器噪声
        Cv2.GaussianBlur(img, img, new Size(1, 1), 1.2);
        img.CopyTo(dst);
        img.Dispose();
    }

    public void Close() => _open = false;
    public void Dispose() => Close();
}
