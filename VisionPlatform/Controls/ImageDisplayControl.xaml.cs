using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using OpenCvSharp;
using VisionPlatform.Models;

namespace VisionPlatform.Controls;

/// <summary>
/// 图像显示控件：实时帧显示 + 缩放/平移 + 十字线 + 像素灰度 + 缺陷叠加。
/// 对外暴露 SetFrame / AddOverlay / ClearOverlays，由 ViewModel 在 UI 线程调用。
/// </summary>
public partial class ImageDisplayControl : UserControl
{
    private WriteableBitmap? _bitmap;
    private byte[] _bgraBuffer = [];
    private int _frameWidth, _frameHeight;
    private DateTime _lastFrameTime;

    public ImageDisplayControl()
    {
        InitializeComponent();
        Loaded += (_, _) => FitToView();
        SizeChanged += (_, _) =>
        {
            if (_bitmap is not null && Math.Abs(Scale.ScaleX - 1) < 0.001) FitToView();
        };
    }

    /// <summary>更新显示帧（UI 线程调用，内部做 BGR→BGRA 转换）。</summary>
    public void SetFrame(Mat frame)
    {
        if (frame is null || frame.Empty()) return;

        // 限流显示，避免 Dispatcher 阻塞
        var now = DateTime.Now;
        if ((now - _lastFrameTime).TotalMilliseconds < 33) return;
        _lastFrameTime = now;

        var w = frame.Width;
        var h = frame.Height;
        var total = w * h * 4;
        if (_bitmap is null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h)
        {
            _bgraBuffer = new byte[total];
            _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            ImageHost.Source = _bitmap;
            _frameWidth = w;
            _frameHeight = h;
            SizeText.Text = $"{w} × {h}";
            SetCanvasSize(w, h);
        }

        if (frame.Channels() == 3)
        {
            using var bgra = new Mat();
            Cv2.CvtColor(frame, bgra, ColorConversionCodes.BGR2BGRA);
            System.Runtime.InteropServices.Marshal.Copy(bgra.Data, _bgraBuffer, 0, total);
        }
        else
        {
            // 灰度图转 BGRA
            using var bgr = new Mat();
            Cv2.CvtColor(frame, bgr, ColorConversionCodes.GRAY2BGRA);
            System.Runtime.InteropServices.Marshal.Copy(bgr.Data, _bgraBuffer, 0, total);
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, w, h), _bgraBuffer, w * 4, 0);
    }

    private void SetCanvasSize(int w, int h)
    {
        ContentCanvas.Width = w;
        ContentCanvas.Height = h;
        if (w == 0) return;
        if (ZoomSlider.Value > 100 && Scale.ScaleX > 1)
        {
            // 保持当前缩放
        }
        else
        {
            FitToView();
        }
    }

    private void FitToView()
    {
        if (_bitmap is null || Scroll.ViewportWidth < 10) return;
        var scale = Math.Min(Scroll.ViewportWidth / _bitmap.PixelWidth,
                             Scroll.ViewportHeight / _bitmap.PixelHeight);
        scale = Math.Clamp(scale * 0.98, 0.05, 4.0);
        ApplyZoom(scale, 0, 0);
    }

    private void ApplyZoom(double scale, double centerX, double centerY)
    {
        scale = Math.Clamp(scale, 0.05, 8.0);
        var oldScale = Scale.ScaleX;
        Scale.ScaleX = Scale.ScaleY = scale;
        ZoomSlider.Value = scale * 100;
        ZoomText.Text = $"{scale * 100:F0}%";

        if (oldScale > 0 && centerX >= 0)
        {
            // 以光标为中心缩放：调整滚动偏移
            var vw = Scroll.ViewportWidth;
            var vh = Scroll.ViewportHeight;
            var ratio = scale / oldScale;
            Scroll.ScrollToHorizontalOffset(centerX * ratio - (centerX - Scroll.HorizontalOffset));
            Scroll.ScrollToVerticalOffset(centerY * ratio - (centerY - Scroll.VerticalOffset));
        }
    }

    // ---------------- 缺陷叠加 ----------------

    private Border? _roiBorder;

    public void ClearOverlays() => OverlayCanvas.Children.Clear();

    /// <summary>显示检测区域 ROI 虚线框（需在 ClearOverlays 之后调用）。</summary>
    public void SetRoi(OpenCvSharp.Rect? roi)
    {
        if (_roiBorder is not null)
        {
            OverlayCanvas.Children.Remove(_roiBorder);
            _roiBorder = null;
        }
        if (roi is null || roi.Value.Width <= 0 || roi.Value.Height <= 0) return;
        var r = roi.Value;
        var border = new Border
        {
            Width = r.Width,
            Height = r.Height,
            BorderBrush = new SolidColorBrush(Color.FromArgb(220, 62, 155, 255)),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false
        };
        var border2 = new Border
        {
            Width = r.Width,
            Height = r.Height,
            BorderBrush = new SolidColorBrush(Color.FromArgb(220, 255, 176, 32)),
            BorderThickness = new Thickness(1, 0, 0, 1),
            IsHitTestVisible = false
        };
        var line = new Line
        {
            X1 = r.X, Y1 = r.Y, X2 = r.X + r.Width, Y2 = r.Y + r.Height,
            Stroke = new SolidColorBrush(Color.FromArgb(160, 62, 155, 255)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 4 },
            IsHitTestVisible = false
        };
        var line2 = new Line
        {
            X1 = r.X + r.Width, Y1 = r.Y, X2 = r.X, Y2 = r.Y + r.Height,
            Stroke = new SolidColorBrush(Color.FromArgb(160, 62, 155, 255)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 4 },
            IsHitTestVisible = false
        };
        var label = new TextBlock
        {
            Text = "ROI 检测区域",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 62, 155, 255)),
            Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
            Padding = new Thickness(4, 1, 4, 1),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(border, r.X);
        Canvas.SetTop(border, r.Y);
        Canvas.SetLeft(line, 0);
        Canvas.SetTop(line, 0);
        Canvas.SetLeft(line2, 0);
        Canvas.SetTop(line2, 0);
        Canvas.SetLeft(label, r.X + 2);
        Canvas.SetTop(label, r.Y + 2);
        OverlayCanvas.Children.Add(border);
        OverlayCanvas.Children.Add(border2);
        OverlayCanvas.Children.Add(line);
        OverlayCanvas.Children.Add(line2);
        OverlayCanvas.Children.Add(label);
        _roiBorder = border;
    }

    public void AddOverlay(Defect defect)
    {
        var color = defect.Type switch
        {
            DetectorType.Scratch => Colors.DeepSkyBlue,
            DetectorType.Template => Colors.Orange,
            DetectorType.Brightness => Colors.Yellow,
            _ => Colors.Red
        };
        AddOverlay(defect.BoundingBox, color, defect.Name);
    }

    public void AddOverlay(OpenCvSharp.Rect rect, Color color, string? label = null)
    {
        var border = new Border
        {
            Width = Math.Max(rect.Width, 2),
            Height = Math.Max(rect.Height, 2),
            BorderBrush = new SolidColorBrush(color),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(1),
            Opacity = 0.95
        };
        Canvas.SetLeft(border, rect.X);
        Canvas.SetTop(border, rect.Y);
        OverlayCanvas.Children.Add(border);

        if (!string.IsNullOrEmpty(label))
        {
            var text = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = new SolidColorBrush(color),
                Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                Padding = new Thickness(3, 1, 3, 1),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(text, rect.X);
            Canvas.SetTop(text, rect.Y - 18 < 0 ? 0 : rect.Y - 18);
            OverlayCanvas.Children.Add(text);
        }
    }

    public void AddCrosshair(OpenCvSharp.Rect rect)
    {
        var line = new Line
        {
            X1 = rect.X, Y1 = rect.Y, X2 = rect.X + rect.Width, Y2 = rect.Y + rect.Height,
            Stroke = new SolidColorBrush(Color.FromArgb(200, 255, 176, 32)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 }
        };
        var line2 = new Line
        {
            X1 = rect.X + rect.Width, Y1 = rect.Y, X2 = rect.X, Y2 = rect.Y + rect.Height,
            Stroke = new SolidColorBrush(Color.FromArgb(200, 255, 176, 32)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 }
        };
        OverlayCanvas.Children.Add(line);
        OverlayCanvas.Children.Add(line2);
    }

    // ---------------- 交互 ----------------

    private void OnZoomSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Scale is null || ZoomText is null) return; // XAML 初始化阶段
        if (Math.Abs(Scale.ScaleX - e.NewValue / 100) > 0.001)
        {
            Scale.ScaleX = Scale.ScaleY = e.NewValue / 100;
            ZoomText.Text = $"{e.NewValue:F0}%";
        }
    }

    private void OnFit(object sender, RoutedEventArgs e) => FitToView();

    private void OnZoomOne(object sender, RoutedEventArgs e) => ApplyZoom(1.0, -1, -1);

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_bitmap is null) return;
        var pos = e.GetPosition(Scroll);
        var delta = e.Delta > 0 ? 1.25 : 0.8;
        ApplyZoom(Scale.ScaleX * delta, pos.X, pos.Y);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pos = e.GetPosition(ContentCanvas);
        if (_bitmap is null || pos.X < 0 || pos.Y < 0 || pos.X >= _frameWidth || pos.Y >= _frameHeight)
        {
            CrossV.Visibility = CrossH.Visibility = Visibility.Collapsed;
            return;
        }

        CrossV.Visibility = CrossH.Visibility = Visibility.Visible;
        var screenPos = e.GetPosition(this);
        Canvas.SetLeft(CrossV, screenPos.X);
        Canvas.SetTop(CrossH, screenPos.Y);

        var x = (int)pos.X;
        var y = (int)pos.Y;
        CoordText.Text = $"X: {x}  Y: {y}";
        var idx = (y * _frameWidth + x) * 4;
        if (_bgraBuffer.Length > idx + 2)
        {
            var gray = (int)(_bgraBuffer[idx] * 0.114 + _bgraBuffer[idx + 1] * 0.587 + _bgraBuffer[idx + 2] * 0.299);
            GrayText.Text = $"灰度: {gray}";
        }
    }
}
