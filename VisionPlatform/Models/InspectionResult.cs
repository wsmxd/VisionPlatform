namespace VisionPlatform.Models;

/// <summary>一次检测的完整结果。</summary>
public class InspectionResult
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public required string ProductName { get; init; }
    public required string SerialNumber { get; init; }
    public required string RecipeName { get; init; }
    public bool IsOk { get; init; }
    public List<Defect> Defects { get; init; } = [];
    public double ElapsedMs { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string? ImagePath { get; set; }
}
