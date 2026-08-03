using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace VisionPlatform.Models;

/// <summary>
/// 检测配方：一组可独立启用的检测项及参数，序列化为 JSON。
/// </summary>
public class Recipe : INotifyPropertyChanged
{
    public string Name { get; set; } = "默认配方";
    public string Description { get; set; } = "";

    // ---------- 相机参数 ----------
    public double Exposure { get; set; } = 2000;
    public double Gain { get; set; } = 1.0;
    public int CameraIndex { get; set; } = 0;
    public int FrameWidth { get; set; } = 0;   // 0 表示相机默认
    public int FrameHeight { get; set; } = 0;

    // ---------- 采集节拍 ----------
    public double TriggerIntervalMs { get; set; } = 500;

    // ---------- 检测区域 ROI（0,0,0,0 = 全图） ----------
    public double RoiX { get; set; } = 200;
    public double RoiY { get; set; } = 140;
    public double RoiW { get; set; } = 560;
    public double RoiH { get; set; } = 440;

    // ---------- 斑点检测（脏污/异物/缺损） ----------
    public bool UseBlob { get; set; } = true;
    public double BlobThreshold { get; set; } = 80;     // 二值化阈值(0-255)，值越小越灵敏
    public double BlobMinArea { get; set; } = 100;      // 最小缺陷面积(px²)
    public double BlobMaxArea { get; set; } = 100000;   // 最大缺陷面积(px²)

    // ---------- 划痕检测 ----------
    public bool UseScratch { get; set; } = true;
    public double ScratchThreshold { get; set; } = 60;   // Canny 双阈值上限
    public double ScratchMinLength { get; set; } = 70;   // 最小划痕长度(px)
    public double ScratchMaxCount { get; set; } = 5;     // 允许的最大划痕条数

    // ---------- 模板匹配（缺件/错位/异物） ----------
    public bool UseTemplate { get; set; }
    public string TemplatePath { get; set; } = "";
    public double TemplateThreshold { get; set; } = 0.80; // 最低相似度

    // ---------- 亮度检测（过暗/过亮/光照不均） ----------
    public bool UseBrightness { get; set; } = true;
    public double BrightnessMin { get; set; } = 70;      // 平均灰度下限
    public double BrightnessMax { get; set; } = 175;     // 平均灰度上限

    [JsonIgnore]
    public string FilePath { get; set; } = "";

    private bool _isCurrent;
    [JsonIgnore]
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value) return;
            _isCurrent = value;
            OnPropertyChanged();
        }
    }

    public Recipe Clone() => (Recipe)MemberwiseClone();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
