using System.Collections.ObjectModel;
using System.IO;
using VisionPlatform.Models;

namespace VisionPlatform.Services.Logging;

/// <summary>日志服务：文件落盘 + UI 环形缓冲。</summary>
public sealed class LogService : IDisposable
{
    private readonly string _logDir;
    private readonly Lock _lock = new();
    private readonly StreamWriter _writer;
    private readonly ObservableCollection<LogEntry> _buffer = [];
    private const int BufferCapacity = 500;

    public event Action<LogEntry>? EntryAdded;

    public LogService(string logDir)
    {
        _logDir = logDir;
        Directory.CreateDirectory(logDir);
        var file = Path.Combine(logDir, $"log_{DateTime.Now:yyyyMMdd}.txt");
        _writer = new StreamWriter(file, append: true) { AutoFlush = true };
    }

    public ObservableCollection<LogEntry> Buffer => _buffer;
    public Lock SyncRoot => _lock;

    public void Debug(string msg) => Write(LogLevel.Debug, msg);
    public void Info(string msg) => Write(LogLevel.Info, msg);
    public void Warn(string msg) => Write(LogLevel.Warn, msg);
    public void Error(string msg) => Write(LogLevel.Error, msg);

    public void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message);
        lock (_lock)
        {
            _writer.WriteLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level}] {entry.Message}");
            _buffer.Add(entry);
            while (_buffer.Count > BufferCapacity)
                _buffer.RemoveAt(0);
        }
        EntryAdded?.Invoke(entry);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer.Flush();
            _writer.Dispose();
        }
    }
}

public record LogEntry(DateTime Timestamp, LogLevel Level, string Message);
