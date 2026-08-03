using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using VisionPlatform.Infrastructure;
using VisionPlatform.Models;

namespace VisionPlatform.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
        var vm = ServiceLocator.History;
        DataContext = vm;
        vm.ShowImageRequested += ShowNgImage;
    }

    private void OnRowDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as DataGrid)?.SelectedItem is InspectionResult result)
            ShowNgImage(result);
    }

    private void ShowNgImage(InspectionResult result)
    {
        if (string.IsNullOrEmpty(result.ImagePath) || !File.Exists(result.ImagePath))
        {
            HistoryStatusText.Text = "该记录无图像存档";
            return;
        }
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(result.ImagePath);
        bitmap.EndInit();
        NgImage.Source = bitmap;
        NgImage.Visibility = Visibility.Visible;
        ImagePlaceholder.Visibility = Visibility.Collapsed;

        DefectListPanel.Children.Clear();
        if (result.Defects.Count == 0)
        {
            DefectListPanel.Children.Add(new TextBlock { Text = "无缺陷 (OK)", Foreground = (System.Windows.Media.Brush)FindResource("OkBrush") });
        }
        foreach (var d in result.Defects)
        {
            var color = d.Type switch
            {
                DetectorType.Scratch => System.Windows.Media.Colors.DeepSkyBlue,
                DetectorType.Template => System.Windows.Media.Colors.Orange,
                DetectorType.Brightness => System.Windows.Media.Colors.Yellow,
                _ => System.Windows.Media.Colors.Red
            };
            var row = new TextBlock
            {
                Text = $"{d.Name}: {d.Detail}",
                Foreground = new System.Windows.Media.SolidColorBrush(color),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 2)
            };
            DefectListPanel.Children.Add(row);
        }
        HistoryStatusText.Text = $"{result.Timestamp:yyyy-MM-dd HH:mm:ss}  {result.SerialNumber}  {result.Width}×{result.Height}";
    }
}
