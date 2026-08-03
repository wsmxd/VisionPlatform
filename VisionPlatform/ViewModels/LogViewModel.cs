using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionPlatform.Infrastructure;
using VisionPlatform.Models;
using VisionPlatform.Services.Logging;

namespace VisionPlatform.ViewModels;

/// <summary>系统日志页 VM。</summary>
public partial class LogViewModel : ObservableObject
{
    private const int MaxEntries = 600;
    private readonly DispatcherTimer _flushTimer;

    public ObservableCollection<LogEntry> Entries { get; } = [];

    [ObservableProperty]
    private LogLevel _filter = LogLevel.Info;

    [ObservableProperty]
    private string _logDir = "";

    private readonly object _lock = new();
    private readonly List<LogEntry> _pending = [];

    public LogViewModel()
    {
        LogDir = Path.Combine(ServiceLocator.DataDir, "Logs");
        LogEntry[] snapshot;
        lock (ServiceLocator.Log.SyncRoot)
            snapshot = ServiceLocator.Log.Buffer.ToArray();
        foreach (var e in snapshot)
            if (e.Level >= Filter) Entries.Add(e);

        // 批量刷新，避免高频日志刷 UI
        _flushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _flushTimer.Tick += (_, _) => Flush();
        _flushTimer.Start();

        ServiceLocator.Log.EntryAdded += e =>
        {
            lock (_lock) _pending.Add(e);
        };
    }

    partial void OnFilterChanged(LogLevel value)
    {
        Entries.Clear();
        LogEntry[] snapshot;
        lock (ServiceLocator.Log.SyncRoot)
            snapshot = ServiceLocator.Log.Buffer.ToArray();
        foreach (var e in snapshot)
            if (e.Level >= value) Entries.Add(e);
    }

    private void Flush()
    {
        List<LogEntry> batch;
        lock (_lock)
        {
            if (_pending.Count == 0) return;
            batch = [.. _pending];
            _pending.Clear();
        }
        foreach (var e in batch)
        {
            if (e.Level < Filter) continue;
            Entries.Add(e);
        }
        while (Entries.Count > MaxEntries)
            Entries.RemoveAt(0);
    }

    [RelayCommand]
    private void Clear() => Entries.Clear();

    [RelayCommand]
    private void OpenLogDir()
    {
        Directory.CreateDirectory(LogDir);
        System.Diagnostics.Process.Start("explorer.exe", LogDir);
    }
}
