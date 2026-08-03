using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VisionPlatform.Controls;

/// <summary>24 小时检测数量柱状图（自绘，无第三方图表库）。</summary>
public partial class HourlyBarChart : UserControl
{
    public static readonly DependencyProperty CountsProperty = DependencyProperty.Register(
        nameof(Counts), typeof(IReadOnlyList<int>), typeof(HourlyBarChart),
        new PropertyMetadata(null, (d, _) => ((HourlyBarChart)d).Redraw()));

    public IReadOnlyList<int>? Counts
    {
        get => (IReadOnlyList<int>?)GetValue(CountsProperty);
        set => SetValue(CountsProperty, value);
    }

    public HourlyBarChart()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Redraw();
    }

    private void Redraw()
    {
        Canvas.Children.Clear();
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 20 || height < 20 || Counts is null) return;

        var max = Counts.DefaultIfEmpty().Max();
        if (max <= 0) max = 1;
        var n = Counts.Count;
        var gap = 2.0;
        var barWidth = Math.Max(1, (width - gap * (n - 1)) / n);

        for (int i = 0; i < n; i++)
        {
            var value = Math.Min(Counts[i], int.MaxValue);
            var barHeight = Math.Max(1, (height - 14) * value / max);
            var brush = i == DateTime.Now.Hour
                ? new SolidColorBrush(Color.FromRgb(62, 155, 255))
                : new SolidColorBrush(Color.FromRgb(45, 62, 84));
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = barWidth,
                Height = barHeight,
                Fill = brush,
                RadiusX = 1,
                RadiusY = 1,
                ToolTip = $"{i}时: {value} 件"
            };
            Canvas.SetLeft(rect, i * (barWidth + gap));
            Canvas.SetBottom(rect, 0);
            Canvas.Children.Add(rect);
        }
    }
}
