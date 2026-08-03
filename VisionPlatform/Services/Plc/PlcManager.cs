using VisionPlatform.Infrastructure;

namespace VisionPlatform.Services.Plc;

/// <summary>
/// PLC 通讯管理器：包装 Modbus 主站、内置从站模拟器与信号地址映射。
/// 产线交互模式：
///   触发信号(读线圈) → 平台执行一次检测 → 写 OK/NG 线圈 → 累加计数寄存器
/// </summary>
public sealed class PlcManager : IDisposable
{
    private readonly ModbusTcpClient _client;

    public PlcManager() : this(new ModbusTcpClient()) { }

    /// <summary>测试/复用场景可注入外部主站客户端。</summary>
    public PlcManager(ModbusTcpClient client)
    {
        _client = client;
    }

    public ModbusTcpClient Client => _client;

    public ModbusTcpServer? SimulatorServer { get; private set; }

    // ------- 信号映射（0 基 Modbus 地址，可由界面修改） -------
    public ushort TriggerCoilAddr { get; set; } = 0;
    public ushort OkCoilAddr { get; set; } = 1;
    public ushort NgCoilAddr { get; set; } = 2;
    public ushort TotalCountRegAddr { get; set; } = 10;   // 32位总计数
    public ushort NgCountRegAddr { get; set; } = 12;      // 32位NG计数

    // ------- 模拟器配置 -------
    public bool SimulatorAutoTrigger { get; set; } = true;
    public double SimulatorTriggerIntervalMs { get; set; } = 1500;

    public bool IsConnected => Client.IsConnected;
    public string? LastError => Client.LastError;
    public string LastHost { get; private set; } = "";
    public int LastPort { get; private set; }

    public event Action? ConnectionChanged;

    private CancellationTokenSource? _autoTriggerCts;

    public bool StartSimulator()
    {
        try
        {
            if (SimulatorServer is not null) return true;
            var server = new ModbusTcpServer(502);
            server.Start();
            SimulatorServer = server;
            Log().Info($"PLC 模拟器已启动 (端口 {server.Port})");
            StartAutoTrigger();
            return true;
        }
        catch (Exception ex)
        {
            Log().Error($"PLC 模拟器启动失败: {ex.Message}");
            return false;
        }
    }

    public void StopSimulator()
    {
        _autoTriggerCts?.Cancel();
        _autoTriggerCts = null;
        SimulatorServer?.Dispose();
        SimulatorServer = null;
    }

    private void StartAutoTrigger()
    {
        if (!SimulatorAutoTrigger) return;
        _autoTriggerCts?.Cancel();
        _autoTriggerCts = new CancellationTokenSource();
        var cts = _autoTriggerCts;
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    if (SimulatorServer is not null && SimulatorAutoTrigger)
                    {
                        SimulatorServer.SetCoil(TriggerCoilAddr, true);
                        await Task.Delay(120, cts.Token);
                        SimulatorServer.SetCoil(TriggerCoilAddr, false);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { }
                try { await Task.Delay((int)SimulatorTriggerIntervalMs, cts.Token); }
                catch (OperationCanceledException) { break; }
            }
        });
    }

    public async Task<bool> ConnectAsync(string host, int port)
    {
        LastHost = host;
        LastPort = port;
        var ok = await Client.ConnectAsync(host, port).ConfigureAwait(false);
        ConnectionChanged?.Invoke();
        return ok;
    }

    public void Disconnect()
    {
        Client.Disconnect();
        ConnectionChanged?.Invoke();
    }

    private static VisionPlatform.Services.Logging.LogService Log() => ServiceLocator.Log;
    public void Dispose()
    {
        StopSimulator();
        Client.Dispose();
    }
}
