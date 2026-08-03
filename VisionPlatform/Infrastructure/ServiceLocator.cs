using System.IO;
using VisionPlatform.Services.Camera;
using VisionPlatform.Services.Logging;
using VisionPlatform.Services.Plc;
using VisionPlatform.Services.Recipes;
using VisionPlatform.Services.Result;
using VisionPlatform.ViewModels;

namespace VisionPlatform.Infrastructure;

/// <summary>
/// 简易服务定位器：集中创建/持有全局单例服务与视图模型。
/// </summary>
public static class ServiceLocator
{
    public static LogService Log { get; private set; } = null!;
    public static RecipeManager Recipes { get; private set; } = null!;
    public static ResultStore Results { get; private set; } = null!;
    public static ReportExporter Reports { get; private set; } = null!;
    public static CameraManager Cameras { get; private set; } = null!;
    public static PlcManager Plc { get; private set; } = null!;

    public static MainViewModel MainViewModel { get; private set; } = null!;
    public static InspectViewModel Inspect { get; private set; } = null!;
    public static RecipeViewModel Recipe { get; private set; } = null!;
    public static HistoryViewModel History { get; private set; } = null!;
    public static PlcViewModel PlcVm { get; private set; } = null!;
    public static LogViewModel LogVm { get; private set; } = null!;

    public static string DataDir { get; private set; } = null!;

    public static void Initialize()
    {
        DataDir = Path.Combine(AppContext.BaseDirectory, "AppData");
        Directory.CreateDirectory(Path.Combine(DataDir, "Images"));
        Directory.CreateDirectory(Path.Combine(DataDir, "Recipes"));

        Log = new LogService(Path.Combine(DataDir, "Logs"));
        Recipes = new RecipeManager(Path.Combine(DataDir, "Recipes"));
        Results = new ResultStore(Path.Combine(DataDir, "VisionPlatform.db"), Path.Combine(DataDir, "Images"));
        Reports = new ReportExporter();
        Cameras = new CameraManager();
        Plc = new PlcManager();

        Recipe = new RecipeViewModel();
        History = new HistoryViewModel();
        PlcVm = new PlcViewModel();
        LogVm = new LogViewModel();
        Inspect = new InspectViewModel();
        MainViewModel = new MainViewModel();

        Log.Info("VisionPlatform 启动完成");
    }

    public static void Shutdown()
    {
        Inspect.Shutdown();
        PlcVm.Shutdown();
        Log.Info("VisionPlatform 退出");
        Log.Dispose();
    }
}
