using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenCvSharp;
using VisionPlatform.Models;

namespace VisionPlatform.Services.Result;

/// <summary>
/// 检测结果存储：SQLite 持久化历史记录，NG 图像落盘存档。
/// </summary>
public sealed class ResultStore
{
    private readonly string _dbPath;
    private readonly string _imageDir;
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ResultStore(string dbPath, string imageDir)
    {
        _dbPath = dbPath;
        _imageDir = imageDir;
        Directory.CreateDirectory(_imageDir);
        Initialize();
    }

    private void Initialize()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Inspections (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp   TEXT NOT NULL,
                ProductName TEXT NOT NULL,
                SerialNumber TEXT NOT NULL,
                RecipeName  TEXT NOT NULL,
                IsOk        INTEGER NOT NULL,
                ElapsedMs   REAL NOT NULL,
                Width       INTEGER NOT NULL,
                Height      INTEGER NOT NULL,
                ImagePath   TEXT,
                DefectsJson TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_Inspections_Timestamp ON Inspections(Timestamp);
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
        conn.Open();
        return conn;
    }

    /// <summary>保存检测结果（NG 图像可选落盘）。</summary>
    public void Insert(InspectionResult result)
    {
        lock (_lock)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Inspections (Timestamp, ProductName, SerialNumber, RecipeName, IsOk, ElapsedMs, Width, Height, ImagePath, DefectsJson)
                VALUES ($t, $p, $s, $r, $ok, $ms, $w, $h, $img, $def);
                """;
            cmd.Parameters.AddWithValue("$t", result.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            cmd.Parameters.AddWithValue("$p", result.ProductName);
            cmd.Parameters.AddWithValue("$s", result.SerialNumber);
            cmd.Parameters.AddWithValue("$r", result.RecipeName);
            cmd.Parameters.AddWithValue("$ok", result.IsOk ? 1 : 0);
            cmd.Parameters.AddWithValue("$ms", result.ElapsedMs);
            cmd.Parameters.AddWithValue("$w", result.Width);
            cmd.Parameters.AddWithValue("$h", result.Height);
            cmd.Parameters.AddWithValue("$img", (object?)result.ImagePath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$def", JsonSerializer.Serialize(result.Defects, _json));
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>NG 图像保存为 JPG，返回文件路径；保存失败返回 null。</summary>
    public string? SaveNgImage(Mat frame, string serialNumber)
    {
        try
        {
            var name = $"{DateTime.Now:yyyyMMdd_HHmmssfff}_{serialNumber.Replace(':', '_')}.jpg";
            var path = Path.Combine(_imageDir, name);
            Cv2.ImWrite(path, frame, new[] { (int)ImwriteFlags.JpegQuality, 90 });
            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    public List<InspectionResult> Query(DateTime from, DateTime to, string? product = null, bool? okOnly = null, int limit = 5000)
    {
        var results = new List<InspectionResult>();
        lock (_lock)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT Timestamp, ProductName, SerialNumber, RecipeName, IsOk, ElapsedMs, Width, Height, ImagePath, DefectsJson
                FROM Inspections
                WHERE Timestamp >= $from AND Timestamp <= $to
                {(product is not null ? "AND ProductName = $p" : "")}
                {(okOnly is not null ? (okOnly.Value ? "AND IsOk = 1" : "AND IsOk = 0") : "")}
                ORDER BY Timestamp DESC
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            cmd.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            if (product is not null) cmd.Parameters.AddWithValue("$p", product);
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new InspectionResult
                {
                    Timestamp = DateTime.Parse(reader.GetString(0)),
                    ProductName = reader.GetString(1),
                    SerialNumber = reader.GetString(2),
                    RecipeName = reader.GetString(3),
                    IsOk = reader.GetInt32(4) == 1,
                    ElapsedMs = reader.GetDouble(5),
                    Width = reader.GetInt32(6),
                    Height = reader.GetInt32(7),
                    ImagePath = reader.IsDBNull(8) ? null : reader.GetString(8),
                    Defects = DeserializeDefects(reader.IsDBNull(9) ? null : reader.GetString(9))
                });
            }
        }
        return results;
    }

    public (int Total, int Ok, int Ng) GetStatistics(DateTime from, DateTime to)
    {
        lock (_lock)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(*), COALESCE(SUM(CASE WHEN IsOk = 1 THEN 1 ELSE 0 END), 0), COALESCE(SUM(CASE WHEN IsOk = 0 THEN 1 ELSE 0 END), 0)
                FROM Inspections WHERE Timestamp >= $from AND Timestamp <= $to;
                """;
            cmd.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            cmd.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            using var reader = cmd.ExecuteReader();
            reader.Read();
            return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
        }
    }

    private static List<Defect> DeserializeDefects(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<Defect>>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>清理早于指定天数的历史记录。</summary>
    public int Prune(DateTime before)
    {
        lock (_lock)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Inspections WHERE Timestamp < $t;";
            cmd.Parameters.AddWithValue("$t", before.ToString("yyyy-MM-dd HH:mm:ss"));
            return cmd.ExecuteNonQuery();
        }
    }
}
