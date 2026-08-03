using System.ComponentModel;

namespace VisionPlatform.Models;

public enum CameraSourceType
{
    [Description("OpenCV 相机")]
    OpenCvCamera,
    [Description("视频文件")]
    VideoFile,
    [Description("模拟相机")]
    Simulated
}

public enum DetectorType
{
    [Description("斑点检测")]
    Blob,
    [Description("划痕检测")]
    Scratch,
    [Description("模板匹配")]
    Template,
    [Description("亮度检测")]
    Brightness
}

public enum LogLevel
{
    [Description("调试")]
    Debug,
    [Description("信息")]
    Info,
    [Description("警告")]
    Warn,
    [Description("错误")]
    Error
}
