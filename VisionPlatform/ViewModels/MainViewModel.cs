using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VisionPlatform.Infrastructure;

namespace VisionPlatform.ViewModels;

/// <summary>主窗口 VM：导航 + 全局状态栏。</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _clockTimer;
    private long _todayTotal, _todayOk, _todayNg;

    public ObservableCollection<NavItem> NavItems { get; } = [];

    [ObservableProperty]
    private object _currentViewModel;

    [ObservableProperty]
    private NavItem? _selectedNav;

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is not null) CurrentViewModel = value.ViewModel;
    }

    [ObservableProperty]
    private string _clockText = "";

    [ObservableProperty]
    private string _todayStatsText = "今日: -";

    [ObservableProperty]
    private string _cameraStatusText = "相机未连接";

    [ObservableProperty]
    private bool _cameraOnline;

    [ObservableProperty]
    private string _plcStatusText = "PLC 未连接";

    [ObservableProperty]
    private bool _plcOnline;

    public MainViewModel()
    {
        CurrentViewModel = ServiceLocator.Inspect;
        NavItems.Add(new NavItem { Icon = "◉", Title = "实时检测", ViewModel = ServiceLocator.Inspect });
        NavItems.Add(new NavItem { Icon = "⚙", Title = "配方管理", ViewModel = ServiceLocator.Recipe });
        NavItems.Add(new NavItem { Icon = "▤", Title = "历史记录", ViewModel = ServiceLocator.History });
        NavItems.Add(new NavItem { Icon = "⇄", Title = "PLC 通讯", ViewModel = ServiceLocator.PlcVm });
        NavItems.Add(new NavItem { Icon = "☰", Title = "系统日志", ViewModel = ServiceLocator.LogVm });

        ServiceLocator.Cameras.CameraOpened += OnCameraOpened;
        ServiceLocator.Cameras.CameraClosed += OnCameraClosed;
        ServiceLocator.Plc.ConnectionChanged += OnPlcChanged;

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => ClockText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        _clockTimer.Start();

        // 加载今日统计
        var (t, ok, ng) = ServiceLocator.Results.GetStatistics(DateTime.Today, DateTime.Now);
        _todayTotal = t; _todayOk = ok; _todayNg = ng;
        UpdateTodayStats();
        ServiceLocator.Inspect.Pipeline.ResultProduced += (r, _) =>
        {
            _todayTotal++;
            if (r.IsOk) _todayOk++; else _todayNg++;
            UpdateTodayStats();
        };
    }

    private void UpdateTodayStats()
        => TodayStatsText = $"今日 {_todayTotal} | OK {_todayOk} | NG {_todayNg} | 良率 {(_todayTotal > 0 ? (double)_todayOk / _todayTotal * 100 : 0):F1}%";

    private void OnCameraOpened()
    {
        CameraStatusText = $"相机在线: {ServiceLocator.Cameras.CurrentItem?.Name}";
        CameraOnline = true;
    }

    private void OnCameraClosed()
    {
        CameraStatusText = "相机未连接";
        CameraOnline = false;
    }

    private void OnPlcChanged()
    {
        PlcOnline = ServiceLocator.Plc.IsConnected;
        PlcStatusText = PlcOnline
            ? $"PLC 在线 ({ServiceLocator.Plc.LastHost}:{ServiceLocator.Plc.LastPort})"
            : "PLC 未连接";
    }
}
