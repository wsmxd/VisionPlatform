using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionPlatform.Infrastructure;
using VisionPlatform.Models;
using VisionPlatform.Services.Result;

namespace VisionPlatform.ViewModels;

/// <summary>历史记录页 VM：条件查询 + 统计 + 报表导出 + NG 图像回看。</summary>
public partial class HistoryViewModel : ObservableObject
{
    public ObservableCollection<InspectionResult> Results { get; } = [];

    public ObservableCollection<string> Products { get; } = [];

    [ObservableProperty]
    private DateTime _fromDate = DateTime.Today.AddDays(-7);

    [ObservableProperty]
    private DateTime _toDate = DateTime.Now;

    [ObservableProperty]
    private string? _selectedProduct;

    [ObservableProperty]
    private bool _showOk = true;

    [ObservableProperty]
    private bool _showNg = true;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _okCount;

    [ObservableProperty]
    private int _ngCount;

    [ObservableProperty]
    private double _yieldPercent;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "未查询";

    public HistoryViewModel()
    {
        Products.Add("全部产品");
        foreach (var r in ServiceLocator.Recipes.Recipes)
        {
            if (!Products.Contains(r.Name)) Products.Add(r.Name);
        }
    }

    [RelayCommand]
    private void Query()
    {
        IsBusy = true;
        StatusText = "查询中...";
        try
        {
            bool? okOnly = ShowOk && !ShowNg ? true : !ShowOk && ShowNg ? false : null;
            var product = SelectedProduct is null || SelectedProduct == "全部产品" ? null : SelectedProduct;
            var list = ServiceLocator.Results.Query(FromDate.Date, ToDate.AddDays(1).AddTicks(-1), product, okOnly);
            Results.Clear();
            foreach (var r in list) Results.Add(r);

            var stats = ServiceLocator.Results.GetStatistics(FromDate.Date, ToDate.AddDays(1).AddTicks(-1));
            TotalCount = stats.Total;
            OkCount = stats.Ok;
            NgCount = stats.Ng;
            YieldPercent = TotalCount > 0 ? (double)OkCount / TotalCount : 0;
            StatusText = $"共 {TotalCount} 条记录 (本次筛选 {Results.Count} 条)";
            ServiceLocator.Log.Info($"历史查询: {FromDate:yyyy-MM-dd} ~ {ToDate:yyyy-MM-dd}, {StatusText}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ExportCsv()
    {
        if (Results.Count == 0) { StatusText = "无数据可导出"; return; }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出 CSV",
            Filter = "CSV 文件|*.csv",
            FileName = $"检测记录_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (dialog.ShowDialog() == true)
        {
            var path = ServiceLocator.Reports.ExportCsv(Results.ToList(), dialog.FileName);
            StatusText = $"已导出: {path}";
            ServiceLocator.Log.Info($"CSV 导出完成: {path}");
        }
    }

    [RelayCommand]
    private void ExportHtml()
    {
        if (Results.Count == 0) { StatusText = "无数据可导出"; return; }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出 HTML 报表",
            Filter = "HTML 文件|*.html",
            FileName = $"检测报表_{DateTime.Now:yyyyMMdd_HHmmss}.html"
        };
        if (dialog.ShowDialog() == true)
        {
            var path = ServiceLocator.Reports.ExportHtml(Results.ToList(), dialog.FileName);
            StatusText = $"已导出: {path}";
            ServiceLocator.Log.Info($"HTML 报表导出完成: {path}");
        }
    }

    [RelayCommand]
    private void PruneOld()
    {
        var before = DateTime.Now.AddDays(-30);
        var n = ServiceLocator.Results.Prune(before);
        StatusText = $"已清理 {before:yyyy-MM-dd} 之前的 {n} 条记录";
        ServiceLocator.Log.Info(StatusText);
    }

    /// <summary>NG 图像回看（View 订阅）。</summary>
    public event Action<InspectionResult>? ShowImageRequested;

    [RelayCommand]
    private void OpenImage(InspectionResult? result)
    {
        if (result is null || string.IsNullOrEmpty(result.ImagePath) || !File.Exists(result.ImagePath))
        {
            StatusText = "该记录无图像存档";
            return;
        }
        ShowImageRequested?.Invoke(result);
    }
}
