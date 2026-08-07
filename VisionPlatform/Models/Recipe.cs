using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace VisionPlatform.Models;

/// <summary>
/// 检测配方：一组可独立启用的检测项及参数，序列化为 JSON。
/// </summary>
public class Recipe : INotifyPropertyChanged
{
    private string _name = "默认配方";
    private string _description = "";
    private double _exposure = 2000;
    private double _gain = 1.0;
    private int _cameraIndex = 0;
    private int _frameWidth = 0;
    private int _frameHeight = 0;
    private double _triggerIntervalMs = 500;
    private double _roiX = 200;
    private double _roiY = 140;
    private double _roiW = 560;
    private double _roiH = 440;
    private bool _useBlob = true;
    private double _blobThreshold = 80;
    private double _blobMinArea = 100;
    private double _blobMaxArea = 100000;
    private bool _useScratch = true;
    private double _scratchThreshold = 60;
    private double _scratchMinLength = 70;
    private double _scratchMaxCount = 5;
    private bool _useTemplate;
    private string _templatePath = "";
    private double _templateThreshold = 0.80;
    private bool _useBrightness = true;
    private double _brightnessMin = 70;
    private double _brightnessMax = 175;
    private string _filePath = "";
    private bool _isCurrent;

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            OnPropertyChanged();
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            if (_description == value) return;
            _description = value;
            OnPropertyChanged();
        }
    }

    // ---------- 相机参数 ----------
    public double Exposure
    {
        get => _exposure;
        set
        {
            if (Math.Abs(_exposure - value) < 0.000001) return;
            _exposure = value;
            OnPropertyChanged();
        }
    }

    public double Gain
    {
        get => _gain;
        set
        {
            if (Math.Abs(_gain - value) < 0.000001) return;
            _gain = value;
            OnPropertyChanged();
        }
    }

    public int CameraIndex
    {
        get => _cameraIndex;
        set
        {
            if (_cameraIndex == value) return;
            _cameraIndex = value;
            OnPropertyChanged();
        }
    }

    public int FrameWidth
    {
        get => _frameWidth;
        set
        {
            if (_frameWidth == value) return;
            _frameWidth = value;
            OnPropertyChanged();
        }
    }

    public int FrameHeight
    {
        get => _frameHeight;
        set
        {
            if (_frameHeight == value) return;
            _frameHeight = value;
            OnPropertyChanged();
        }
    }

    // ---------- 采集节拍 ----------
    public double TriggerIntervalMs
    {
        get => _triggerIntervalMs;
        set
        {
            if (Math.Abs(_triggerIntervalMs - value) < 0.000001) return;
            _triggerIntervalMs = value;
            OnPropertyChanged();
        }
    }

    // ---------- 检测区域 ROI（0,0,0,0 = 全图） ----------
    public double RoiX
    {
        get => _roiX;
        set
        {
            if (Math.Abs(_roiX - value) < 0.000001) return;
            _roiX = value;
            OnPropertyChanged();
        }
    }

    public double RoiY
    {
        get => _roiY;
        set
        {
            if (Math.Abs(_roiY - value) < 0.000001) return;
            _roiY = value;
            OnPropertyChanged();
        }
    }

    public double RoiW
    {
        get => _roiW;
        set
        {
            if (Math.Abs(_roiW - value) < 0.000001) return;
            _roiW = value;
            OnPropertyChanged();
        }
    }

    public double RoiH
    {
        get => _roiH;
        set
        {
            if (Math.Abs(_roiH - value) < 0.000001) return;
            _roiH = value;
            OnPropertyChanged();
        }
    }

    // ---------- 斑点检测（脏污/异物/缺损） ----------
    public bool UseBlob
    {
        get => _useBlob;
        set
        {
            if (_useBlob == value) return;
            _useBlob = value;
            OnPropertyChanged();
        }
    }

    public double BlobThreshold
    {
        get => _blobThreshold;
        set
        {
            if (Math.Abs(_blobThreshold - value) < 0.000001) return;
            _blobThreshold = value;
            OnPropertyChanged();
        }
    }

    public double BlobMinArea
    {
        get => _blobMinArea;
        set
        {
            if (Math.Abs(_blobMinArea - value) < 0.000001) return;
            _blobMinArea = value;
            OnPropertyChanged();
        }
    }

    public double BlobMaxArea
    {
        get => _blobMaxArea;
        set
        {
            if (Math.Abs(_blobMaxArea - value) < 0.000001) return;
            _blobMaxArea = value;
            OnPropertyChanged();
        }
    }

    // ---------- 划痕检测 ----------
    public bool UseScratch
    {
        get => _useScratch;
        set
        {
            if (_useScratch == value) return;
            _useScratch = value;
            OnPropertyChanged();
        }
    }

    public double ScratchThreshold
    {
        get => _scratchThreshold;
        set
        {
            if (Math.Abs(_scratchThreshold - value) < 0.000001) return;
            _scratchThreshold = value;
            OnPropertyChanged();
        }
    }

    public double ScratchMinLength
    {
        get => _scratchMinLength;
        set
        {
            if (Math.Abs(_scratchMinLength - value) < 0.000001) return;
            _scratchMinLength = value;
            OnPropertyChanged();
        }
    }

    public double ScratchMaxCount
    {
        get => _scratchMaxCount;
        set
        {
            if (Math.Abs(_scratchMaxCount - value) < 0.000001) return;
            _scratchMaxCount = value;
            OnPropertyChanged();
        }
    }

    // ---------- 模板匹配（缺件/错位/异物） ----------
    public bool UseTemplate
    {
        get => _useTemplate;
        set
        {
            if (_useTemplate == value) return;
            _useTemplate = value;
            OnPropertyChanged();
        }
    }

    public string TemplatePath
    {
        get => _templatePath;
        set
        {
            if (_templatePath == value) return;
            _templatePath = value;
            OnPropertyChanged();
        }
    }

    public double TemplateThreshold
    {
        get => _templateThreshold;
        set
        {
            if (Math.Abs(_templateThreshold - value) < 0.000001) return;
            _templateThreshold = value;
            OnPropertyChanged();
        }
    }

    // ---------- 亮度检测（过暗/过亮/光照不均） ----------
    public bool UseBrightness
    {
        get => _useBrightness;
        set
        {
            if (_useBrightness == value) return;
            _useBrightness = value;
            OnPropertyChanged();
        }
    }

    public double BrightnessMin
    {
        get => _brightnessMin;
        set
        {
            if (Math.Abs(_brightnessMin - value) < 0.000001) return;
            _brightnessMin = value;
            OnPropertyChanged();
        }
    }

    public double BrightnessMax
    {
        get => _brightnessMax;
        set
        {
            if (Math.Abs(_brightnessMax - value) < 0.000001) return;
            _brightnessMax = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public string FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath == value) return;
            _filePath = value;
            OnPropertyChanged();
        }
    }

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
