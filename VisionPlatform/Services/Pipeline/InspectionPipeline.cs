using System.Diagnostics;
using System.Threading.Channels;
using OpenCvSharp;
using VisionPlatform.Infrastructure;
using VisionPlatform.Models;
using VisionPlatform.Services.Camera;
using VisionPlatform.Services.Detection;
using VisionPlatform.Services.Logging;
using VisionPlatform.Services.Plc;
using VisionPlatform.Services.Result;

namespace VisionPlatform.Services.Pipeline;

public enum TriggerMode
{
    [System.ComponentModel.Description("手动触发")]
    Manual,
    [System.ComponentModel.Description("定时触发")]
    Interval,
    [System.ComponentModel.Description("PLC 触发")]
    PlcTrigger
}

/// <summary>
/// 检测流水线（生产者-消费者架构）：
///   采集线程：相机抓帧 → 实时预览（丢帧保新）
///   检测线程：触发后从队列取帧 → 检测 → 结果落库/PLC 反馈
/// 队列深度固定，检测跟不上时自动丢帧，保证实时性。
/// </summary>
public sealed class InspectionPipeline : IDisposable
{
    private readonly Channel<(Mat frame, string serial)> _queue =
        Channel.CreateBounded<(Mat, string)>(new BoundedChannelOptions(2)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private Task? _processTask;
    private bool _busy;

    public bool IsRunning => _captureTask is not null && !_captureTask.IsCompleted;

    public TriggerMode TriggerMode { get; set; } = TriggerMode.Interval;
    public double IntervalMs { get; set; } = 1000;
    public long TriggerCount { get; private set; }
    public long OkCount { get; private set; }
    public long NgCount { get; private set; }
    public double LastElapsedMs { get; private set; }
    public int Fps { get; private set; }

    public string? CurrentRecipeName { get; private set; }

    /// <summary>PLC 信号源（默认取全局 ServiceLocator，测试可注入）。</summary>
    public Func<PlcManager>? PlcSource { get; set; }

    private PlcManager Plc => PlcSource?.Invoke() ?? ServiceLocator.Plc;

    /// <summary>日志源（测试可注入）。</summary>
    public Func<LogService>? LogSource { get; set; }

    private LogService Log => LogSource?.Invoke() ?? ServiceLocator.Log;

    /// <summary>检测器流水线（流水线自持，默认新建）。</summary>
    public DetectorPipeline Detector { get; } = new();

    /// <summary>结果存储（null 时不落库，默认取全局 ServiceLocator）。</summary>
    public ResultStore? ResultStore { get; set; }

    private ResultStore Store => ResultStore ?? ServiceLocator.Results;

    /// <summary>新帧预览（Mat 由接收方负责 Dispose；已限流 ~15fps）。</summary>
    public event Action<Mat>? FramePreview;

    /// <summary>
    /// 完成一次检测。第二参数为被检测的原始帧（同步回调内完成拷贝，
    /// 回调返回后由流水线释放）。第三参数表示是否为流水线推送的新结果。
    /// </summary>
    public event Action<InspectionResult, Mat>? ResultProduced;

    public event Action? StateChanged;
    public event Action? Triggered;

    private DateTime _lastTriggerAt = DateTime.MinValue;
    private bool _plcPrevTrigger;

    public void Start(Recipe recipe, CameraManager cameras)
    {
        if (IsRunning) return;

        // 打开相机
        if (cameras.CurrentItem is null)
        {
            Log.Warn("未选择相机源");
            return;
        }
        if (!cameras.Open(cameras.CurrentItem, recipe))
        {
            Log.Error($"相机打开失败: {cameras.CurrentItem.Name}");
            return;
        }

        CurrentRecipeName = recipe.Name;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _captureTask = Task.Run(() => CaptureLoop(cameras, token));
        _processTask = Task.Run(() => ProcessLoop(recipe, token));
        Log.Info($"检测流水线已启动 (配方: {recipe.Name})");
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _cts?.Cancel();
        try
        {
            Task.WaitAll([_captureTask!, _processTask!], 3000);
        }
        catch (AggregateException) { }
        _captureTask = null;
        _processTask = null;
        _cts?.Dispose();
        _cts = null;
        Log.Info("检测流水线已停止");
        StateChanged?.Invoke();
    }

    // ---------------- 采集线程 ----------------
    private void CaptureLoop(CameraManager cameras, CancellationToken token)
    {
        var sw = Stopwatch.StartNew();
        long frames = 0;
        var previewWindow = TimeSpan.FromMilliseconds(66); // ~15fps 预览
        var lastPreview = DateTime.MinValue;

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (cameras.CurrentCamera is null || !cameras.CurrentCamera.IsOpen)
                {
                    Task.Delay(50, token).Wait(token);
                    continue;
                }
                if (!cameras.CurrentCamera.TryGrab(out var frame))
                {
                    Task.Delay(5, token).Wait(token);
                    continue;
                }

                // 预览（限流）
                var now = DateTime.Now;
                if (now - lastPreview >= previewWindow)
                {
                    lastPreview = now;
                    FramePreview?.Invoke(frame.Clone());
                }

                // 触发逻辑
                var shouldTrigger = ShouldTrigger(now);
                if (shouldTrigger && !_busy && _queue.Reader.Count < 2)
                {
                    _busy = true;
                    _lastTriggerAt = now;
                    TriggerCount++;
                    Triggered?.Invoke();
                    _queue.Writer.TryWrite((frame, NextSerial()));
                    frame = null!; // 所有权移交检测线程
                }
                frame?.Dispose();

                frames++;
                if (sw.ElapsedMilliseconds >= 1000)
                {
                    Fps = (int)(frames * 1000.0 / sw.ElapsedMilliseconds);
                    frames = 0;
                    sw.Restart();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error($"采集线程异常: {ex.Message}");
            }
        }
    }

    private bool ShouldTrigger(DateTime now)
    {
        switch (TriggerMode)
        {
            case TriggerMode.Interval:
                return (now - _lastTriggerAt).TotalMilliseconds >= IntervalMs;
            case TriggerMode.PlcTrigger:
            {
                if (Plc.Client.IsConnected)
                {
                    try
                    {
                        var coil = Plc.Client.ReadCoilAsync(Plc.TriggerCoilAddr).GetAwaiter().GetResult();
                        var rising = coil && !_plcPrevTrigger;
                        _plcPrevTrigger = coil;
                        return rising;
                    }
                    catch
                    {
                        return false;
                    }
                }
                return false;
            }
            default:
                return false;
        }
    }

    private long _serialSeq = DateTime.Now.Ticks % 100000;

    private string NextSerial() => $"{DateTime.Now:yyyyMMdd-HHmmss}-{++_serialSeq % 9999:0000}";

    // ---------------- 检测线程 ----------------
    private void ProcessLoop(Recipe recipe, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!_queue.Reader.WaitToReadAsync(token).AsTask().GetAwaiter().GetResult()) break;
                if (!_queue.Reader.TryRead(out var item)) continue;
                try
                {
                    var sw = Stopwatch.StartNew();
                    InspectionResult result;
                    try
                    {
                        result = Detector.Inspect(item.frame, recipe, item.serial);
                    }
                    finally
                    {
                        sw.Stop();
                    }
                    LastElapsedMs = sw.Elapsed.TotalMilliseconds;

                    // NG 图像存档
                    if (!result.IsOk)
                        result.ImagePath = Store.SaveNgImage(item.frame, result.SerialNumber);

                    // 统计与落库
                    if (result.IsOk) OkCount++; else NgCount++;
                    Store.Insert(result);
                    ReportToPlcAsync(result).ConfigureAwait(false).GetAwaiter().GetResult();

                    // 通知 UI（同步回调，接收方在回调内拷贝帧）
                    ResultProduced?.Invoke(result, item.frame);
                }
                finally
                {
                    _busy = false;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _busy = false;
                Log.Error($"检测线程异常: {ex.Message}");
            }
        }
    }

    private async Task ReportToPlcAsync(InspectionResult result)
    {
        var plc = Plc;
        if (!plc.Client.IsConnected) return;
        try
        {
            var coil = result.IsOk ? plc.OkCoilAddr : plc.NgCoilAddr;
            await plc.Client.WriteCoilAsync(coil, true).ConfigureAwait(false);
            await Task.Delay(60).ConfigureAwait(false);
            await plc.Client.WriteCoilAsync(coil, false).ConfigureAwait(false);

            var total = (uint)OkCount + (uint)NgCount;
            await plc.Client.WriteRegistersAsync(plc.TotalCountRegAddr,
                [(ushort)(total >> 16), (ushort)(total & 0xFFFF)]).ConfigureAwait(false);
            await plc.Client.WriteRegistersAsync(plc.NgCountRegAddr,
                [(ushort)(NgCount >> 16), (ushort)(NgCount & 0xFFFF)]).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warn($"PLC 反馈失败: {ex.Message}");
        }
    }

    public void ManualTrigger() => _lastTriggerAt = DateTime.MinValue;

    public void Dispose()
    {
        Stop();
        Detector.Dispose();
    }
}
