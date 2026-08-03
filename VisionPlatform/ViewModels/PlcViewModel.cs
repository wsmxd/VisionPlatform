using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionPlatform.Infrastructure;
using VisionPlatform.Services.Plc;

namespace VisionPlatform.ViewModels;

/// <summary>PLC 通讯页 VM：连接管理、信号映射、IO 测试、模拟器。</summary>
public partial class PlcViewModel : ObservableObject
{
    [ObservableProperty]
    private string _host = "127.0.0.1";

    [ObservableProperty]
    private int _port = 502;

    [ObservableProperty]
    private byte _unitId = 1;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionInfo = "未连接";

    [ObservableProperty]
    private bool _simulatorRunning;

    [ObservableProperty]
    private bool _simulatorAutoTrigger = true;

    [ObservableProperty]
    private double _simulatorIntervalMs = 1500;

    [ObservableProperty]
    private string _testResult = "";

    [ObservableProperty]
    private bool _testBusy;

    [ObservableProperty]
    private bool _running;

    // 信号映射
    [ObservableProperty]
    private ushort _triggerCoilAddr = 0;

    [ObservableProperty]
    private ushort _okCoilAddr = 1;

    [ObservableProperty]
    private ushort _ngCoilAddr = 2;

    [ObservableProperty]
    private ushort _totalCountRegAddr = 10;

    [ObservableProperty]
    private ushort _ngCountRegAddr = 12;

    public PlcViewModel()
    {
        var plc = ServiceLocator.Plc;
        plc.ConnectionChanged += OnConnectionChanged;
        ApplySettingsToPlc();
    }

    private void OnConnectionChanged()
    {
        IsConnected = ServiceLocator.Plc.IsConnected;
        ConnectionInfo = IsConnected
            ? $"已连接 {ServiceLocator.Plc.LastHost}:{ServiceLocator.Plc.LastPort}"
            : $"连接断开: {ServiceLocator.Plc.LastError}";
    }

    partial void OnSimulatorAutoTriggerChanged(bool value) => ServiceLocator.Plc.SimulatorAutoTrigger = value;
    partial void OnSimulatorIntervalMsChanged(double value) => ServiceLocator.Plc.SimulatorTriggerIntervalMs = value;
    partial void OnTriggerCoilAddrChanged(ushort value) => SyncAddr();
    partial void OnOkCoilAddrChanged(ushort value) => SyncAddr();
    partial void OnNgCoilAddrChanged(ushort value) => SyncAddr();
    partial void OnTotalCountRegAddrChanged(ushort value) => SyncAddr();
    partial void OnNgCountRegAddrChanged(ushort value) => SyncAddr();

    private void ApplySettingsToPlc()
    {
        ServiceLocator.Plc.SimulatorAutoTrigger = SimulatorAutoTrigger;
        ServiceLocator.Plc.SimulatorTriggerIntervalMs = SimulatorIntervalMs;
        SyncAddr();
    }

    private void SyncAddr()
    {
        var plc = ServiceLocator.Plc;
        plc.TriggerCoilAddr = TriggerCoilAddr;
        plc.OkCoilAddr = OkCoilAddr;
        plc.NgCoilAddr = NgCoilAddr;
        plc.TotalCountRegAddr = TotalCountRegAddr;
        plc.NgCountRegAddr = NgCountRegAddr;
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        IsConnected = false;
        TestResult = $"正在连接 {Host}:{Port} ...";
        var ok = await ServiceLocator.Plc.ConnectAsync(Host, Port);
        TestResult = ok ? $"连接成功: {Host}:{Port}" : $"连接失败: {ServiceLocator.Plc.LastError}";
        if (ok)
            ServiceLocator.Log.Info($"PLC 已连接: {Host}:{Port}");
        else
            ServiceLocator.Log.Warn($"PLC 连接失败: {ServiceLocator.Plc.LastError}");
    }

    [RelayCommand]
    private void Disconnect()
    {
        ServiceLocator.Plc.Disconnect();
        TestResult = "已断开连接";
    }

    [RelayCommand]
    private void StartSimulator()
    {
        if (ServiceLocator.Plc.StartSimulator())
        {
            SimulatorRunning = true;
            TestResult = $"PLC 模拟器已启动 (127.0.0.1:{Port})，可连接测试";
        }
    }

    [RelayCommand]
    private void StopSimulator()
    {
        ServiceLocator.Plc.StopSimulator();
        SimulatorRunning = false;
        TestResult = "模拟器已停止";
    }

    // ---------------- IO 测试 ----------------

    [RelayCommand]
    private void TestTriggerCoil()
    {
        var plc = ServiceLocator.Plc;
        if (!plc.Client.IsConnected) { TestResult = "未连接 PLC"; return; }
        try
        {
            var v = plc.Client.ReadCoilAsync(plc.TriggerCoilAddr).GetAwaiter().GetResult();
            TestResult = $"触发线圈 (地址 {plc.TriggerCoilAddr}): {(v ? "TRUE" : "FALSE")}";
        }
        catch (Exception ex)
        {
            TestResult = $"读取失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void TestWriteOkNg()
    {
        var plc = ServiceLocator.Plc;
        if (!plc.Client.IsConnected) { TestResult = "未连接 PLC"; return; }
        try
        {
            plc.Client.WriteCoilAsync(plc.OkCoilAddr, true).GetAwaiter().GetResult();
            plc.Client.WriteCoilAsync(plc.OkCoilAddr, false).GetAwaiter().GetResult();
            TestResult = $"OK 线圈 (地址 {plc.OkCoilAddr}) 写入测试成功";
        }
        catch (Exception ex)
        {
            TestResult = $"写入失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void TestReadRegisters()
    {
        var plc = ServiceLocator.Plc;
        if (!plc.Client.IsConnected) { TestResult = "未连接 PLC"; return; }
        try
        {
            var regs = plc.Client.ReadRegistersAsync(plc.TotalCountRegAddr, 4).GetAwaiter().GetResult();
            TestResult = $"计数寄存器 (地址 {plc.TotalCountRegAddr}): " +
                         $"[{string.Join(", ", regs)}] (总={regs[0] << 16 | regs[1]}, NG={regs[2] << 16 | regs[3]})";
        }
        catch (Exception ex)
        {
            TestResult = $"读取失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void TestPulseTrigger()
    {
        var plc = ServiceLocator.Plc;
        if (!plc.Client.IsConnected) { TestResult = "未连接 PLC"; return; }
        try
        {
            plc.Client.WriteCoilAsync(plc.TriggerCoilAddr, true).GetAwaiter().GetResult();
            Task.Delay(80).GetAwaiter().GetResult();
            plc.Client.WriteCoilAsync(plc.TriggerCoilAddr, false).GetAwaiter().GetResult();
            TestResult = $"已向触发线圈 (地址 {plc.TriggerCoilAddr}) 发送脉冲";
        }
        catch (Exception ex)
        {
            TestResult = $"写入失败: {ex.Message}";
        }
    }

    public void Shutdown() => ServiceLocator.Plc.Dispose();
}
