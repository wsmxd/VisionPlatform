using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using VisionPlatform.Infrastructure;
using VisionPlatform.Models;
using VisionPlatform.Services.Camera;
using VisionPlatform.Services.Pipeline;

namespace VisionPlatform.ViewModels;

/// <summary>结果列表项（轻量展示模型）。</summary>
public class ResultItem
{
    public DateTime Time { get; init; }
    public required string Serial { get; init; }
    public bool IsOk { get; init; }
    public double ElapsedMs { get; init; }
    public required string DefectSummary { get; init; }
    public string? ImagePath { get; init; }
    public int DefectCount { get; init; }
    public string SimDefectInfo { get; init; } = "";
}

/// <summary>实时检测页面 VM。</summary>
public partial class InspectViewModel : ObservableObject
{
    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _statusTimer;
    private readonly object _frameLock = new(); // 保护 _lastFullFrame（采集线程/UI 线程共享）
    private Mat? _lastFullFrame;      // 最近一帧完整图像（用于截取模板，VM 独占所有权）
    private bool _freezeUntilTrigger; // 结果帧冻结显示

    public InspectionPipeline Pipeline { get; } = new();

    public ObservableCollection<CameraItem> Cameras => ServiceLocator.Cameras.AvailableCameras;
    public ObservableCollection<ResultItem> RecentResults { get; } = [];
    public ObservableCollection<int> HourlyCounts { get; } = new(Enumerable.Repeat(0, 24));
    public static string[] Hours { get; } = [.. Enumerable.Range(0, 24).Select(i => $"{i:D2}时")];
    public ObservableCollection<TriggerMode> TriggerModes { get; } = [.. Enum.GetValues<TriggerMode>()];

    /// <summary>UI 线程帧就绪（显示用）。</summary>
    public event Action<Mat>? FrameReady;

    [ObservableProperty]
    private CameraItem? _selectedCamera;

    [ObservableProperty]
    private Recipe _recipe;

    [ObservableProperty]
    private TriggerMode _triggerMode = TriggerMode.Interval;

    [ObservableProperty]
    private double _intervalMs = 1000;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isCameraOpen;

    [ObservableProperty]
    private string _cameraStatus = "相机未连接";

    [ObservableProperty]
    private string _runStatus = "待机";

    [ObservableProperty]
    private int _fps;

    [ObservableProperty]
    private double _lastElapsedMs;

    [ObservableProperty]
    private long _sessionTotal;

    [ObservableProperty]
    private long _sessionOk;

    [ObservableProperty]
    private long _sessionNg;

    [ObservableProperty]
    private double _sessionYield;

    public InspectViewModel()
    {
        Pipeline.ResultStore = ServiceLocator.Results;
        Recipe = ServiceLocator.Recipes.CurrentRecipe;
        SelectedCamera = Cameras.FirstOrDefault();

        ServiceLocator.Recipes.CurrentRecipeChanged += r => Recipe = r;
        ServiceLocator.Cameras.CameraOpened += () =>
        {
            IsCameraOpen = true;
            CameraStatus = $"相机在线: {ServiceLocator.Cameras.CurrentItem?.Name}";
            StartPreview();
        };
        ServiceLocator.Cameras.CameraClosed += () =>
        {
            IsCameraOpen = false;
            CameraStatus = "相机未连接";
            StopPreview();
        };

        Pipeline.FramePreview += OnPipelineFrame;
        Pipeline.ResultProduced += OnResultProduced;
        Pipeline.Triggered += () => _freezeUntilTrigger = false;
        Pipeline.StateChanged += () => UpdateRunState();

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _previewTimer.Tick += (_, _) => PollPreviewFrame();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _statusTimer.Tick += (_, _) => UpdateMetrics();
        _statusTimer.Start();

        LoadTodayHistory();
    }

    // ---------------- 相机 ----------------

    private void StartPreview() => _previewTimer.Start();
    private void StopPreview() => _previewTimer.Stop();

    private void PollPreviewFrame()
    {
        if (Pipeline.IsRunning) return;
        var cam = ServiceLocator.Cameras.CurrentCamera;
        if (cam is null || !cam.IsOpen) return;
        if (cam.TryGrab(out var frame))
        {
            try
            {
                var view = frame.Clone();       // 交给 UI 显示（View 负责释放）
                lock (_frameLock)
                {
                    _lastFullFrame?.Dispose();
                    _lastFullFrame = frame.Clone(); // VM 自持副本，供截取模板
                }
                if (FrameReady is null) view.Dispose();
                else FrameReady?.Invoke(view);
            }
            finally
            {
                frame.Dispose();
            }
        }
    }

    [RelayCommand]
    private async Task ScanCamerasAsync()
    {
        CameraStatus = "扫描中...";
        await ServiceLocator.Cameras.ScanOpenCvCamerasAsync();
        CameraStatus = IsCameraOpen ? $"相机在线: {ServiceLocator.Cameras.CurrentItem?.Name}" : "相机未连接";
        OnPropertyChanged(nameof(Cameras));
    }

    [RelayCommand]
    private void BrowseVideoFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择视频文件",
            Filter = "视频文件|*.mp4;*.avi;*.mkv;*.mov;*.wmv|所有文件|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            ServiceLocator.Cameras.SetVideoFile(dialog.FileName);
            OnPropertyChanged(nameof(Cameras));
        }
    }

    [RelayCommand]
    private void OpenCamera()
    {
        if (SelectedCamera is null)
        {
            ServiceLocator.Log.Warn("请先选择相机源");
            return;
        }
        ServiceLocator.Cameras.Open(SelectedCamera, Recipe);
    }

    [RelayCommand]
    private void CloseCamera()
    {
        StopPreview();
        ServiceLocator.Cameras.Close();
        ClearOverlays();
    }

    // ---------------- 运行控制 ----------------

    [RelayCommand]
    private void Start()
    {
        if (Pipeline.IsRunning) return;
        Pipeline.TriggerMode = TriggerMode;
        Pipeline.IntervalMs = IntervalMs;
        Pipeline.Start(Recipe, ServiceLocator.Cameras);
        if (Pipeline.IsRunning)
        {
            IsRunning = true;
            RunStatus = "运行中";
        }
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(TriggerMode));
    }

    [RelayCommand]
    private void Stop()
    {
        if (!Pipeline.IsRunning) return;
        Pipeline.Stop();
        IsRunning = false;
        RunStatus = "已停止";
        ClearOverlays();
        OnPropertyChanged(nameof(IsRunning));
    }

    [RelayCommand]
    private void ManualTrigger() => Pipeline.ManualTrigger();

    [RelayCommand]
    private void ClearSessionStats()
    {
        SessionTotal = SessionOk = SessionNg = 0;
        SessionYield = 0;
    }

    [RelayCommand]
    private void SaveTemplate()
    {
        Mat tpl;
        lock (_frameLock)
        {
            if (_lastFullFrame is null)
            {
                ServiceLocator.Log.Warn("尚无可用帧，请先打开相机");
                return;
            }
            tpl = _lastFullFrame.Clone(); // 加锁取出副本，避免被采集线程替换/释放
        }
        try
        {
            var dir = Path.Combine(ServiceLocator.DataDir, "Templates");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{Recipe.Name}_{DateTime.Now:HHmmss}.png");
            if (Cv2.ImWrite(path, tpl))
            {
                Recipe.TemplatePath = path;
                ServiceLocator.Recipes.Save(Recipe);
                ServiceLocator.Log.Info($"模板已保存: {path}");
            }
        }
        finally
        {
            tpl.Dispose();
        }
    }

    // ---------------- 流水线事件 ----------------

    private void OnPipelineFrame(Mat frame)
    {
        if (_freezeUntilTrigger) return; // 结果帧冻结期间不刷新
        var view = frame.Clone();       // 交给 UI 显示（View 负责释放）
        var tpl = frame.Clone();        // VM 自持副本，供截取模板
        lock (_frameLock)
        {
            _lastFullFrame?.Dispose();
            _lastFullFrame = tpl;
        }
        var ui = Application.Current?.Dispatcher;
        if (ui is null || ui.HasShutdownStarted)
        {
            view.Dispose();
            return;
        }
        ui.BeginInvoke(() =>
        {
            ClearOverlays();
            if (FrameReady is null) view.Dispose();
            else FrameReady?.Invoke(view);
        });
    }

    private void OnResultProduced(InspectionResult result, Mat frame)
    {
        var snapshot = frame.Clone();
        _freezeUntilTrigger = true;
        var ui = Application.Current?.Dispatcher;
        if (ui is null || ui.HasShutdownStarted)
        {
            snapshot.Dispose();
            return;
        }
        ui.BeginInvoke(() => ShowResult(result, snapshot));
    }

    private void ShowResult(InspectionResult result, Mat frame)
    {
        var view = frame.Clone();       // 交给 UI 显示（View 负责释放）
        lock (_frameLock)
        {
            _lastFullFrame?.Dispose();
            _lastFullFrame = frame.Clone(); // VM 自持副本，供截取模板
        }
        frame.Dispose();

        ClearOverlays();
        foreach (var d in result.Defects)
            AddOverlay(d);
        if (FrameReady is null) view.Dispose();
        else FrameReady?.Invoke(view);

        RecentResults.Insert(0, new ResultItem
        {
            Time = result.Timestamp,
            Serial = result.SerialNumber,
            IsOk = result.IsOk,
            ElapsedMs = result.ElapsedMs,
            DefectCount = result.Defects.Count,
            DefectSummary = result.Defects.Count > 0
                ? string.Join("；", result.Defects.Select(d => d.Name).Distinct())
                : "-",
            ImagePath = result.ImagePath,
            SimDefectInfo = (ServiceLocator.Cameras.CurrentCamera as SimulatedCamera)?.LastFrameHasDefect == true
                ? $"模拟: {(ServiceLocator.Cameras.CurrentCamera as SimulatedCamera)?.LastFrameDefectInfo}"
                : ""
        });
        while (RecentResults.Count > 200) RecentResults.RemoveAt(RecentResults.Count - 1);

        SessionTotal++;
        if (result.IsOk) SessionOk++; else SessionNg++;
        SessionYield = SessionTotal > 0 ? (double)SessionOk / SessionTotal : 0;
        UpdateHourly(result.Timestamp);
    }

    private void AddOverlay(Defect d)
    {
        // 由 View 通过回调实现（ImageDisplayControl 在 View 中持有）
        OverlayRequested?.Invoke(d);
    }

    /// <summary>请求叠加一个缺陷框（View 订阅）。</summary>
    public event Action<Defect>? OverlayRequested;

    private void ClearOverlays() => OverlaysCleared?.Invoke();
    public event Action? OverlaysCleared;

    private void UpdateHourly(DateTime time)
    {
        var hour = time.Hour;
        if (hour >= 0 && hour < 24)
        {
            HourlyCounts[hour] = HourlyCounts[hour] + 1;
        }
    }

    private void LoadTodayHistory()
    {
        var today = DateTime.Today;
        for (var h = 0; h < 24; h++)
        {
            var from = today.AddHours(h);
            var to = from.AddHours(1);
            HourlyCounts[h] = ServiceLocator.Results.Query(from, to, limit: 10000).Count;
        }
    }

    private void UpdateMetrics()
    {
        Fps = Pipeline.Fps;
        LastElapsedMs = Pipeline.LastElapsedMs;
    }

    private void UpdateRunState() => RunStatus = Pipeline.IsRunning ? "运行中" : "待机";

    public void Shutdown()
    {
        _previewTimer.Stop();
        _statusTimer.Stop();
        Pipeline.Dispose();
        lock (_frameLock)
        {
            _lastFullFrame?.Dispose();
            _lastFullFrame = null;
        }
    }
}
