# VisionPlatform

VisionPlatform 是一个基于 WPF + OpenCV 的视觉检测演示平台，主要用于演示工业视觉场景下的“图像采集 → 缺陷检测 → 结果落库 → UI 展示”完整流程。

## 项目简介

该项目提供了一个可运行的视觉检测样板，支持：

- 相机/视频源接入
- 模拟相机演示模式
- 多种基础视觉检测算法
- 配方管理与参数配置
- 检测结果保存与历史查询
- PLC 通讯联动（可选）

适合用于学习、演示或作为工业视觉项目的前端/流程骨架。

## 功能特性

### 1. 图像采集
- 支持 OpenCV 相机接入
- 支持视频文件输入
- 支持模拟相机，无需真实硬件也可运行

### 2. 检测算法
项目内置了几种基础视觉检测器：

- 斑点检测：用于检测污点、异物、局部缺损
- 划痕检测：用于检测线状缺陷
- 模板匹配：用于判断工件是否与标准模板一致
- 亮度检测：用于判断图像过暗/过亮

### 3. 配方管理
- 支持保存检测配方
- 可配置 ROI、阈值、最小面积、模板路径等参数
- 配方可作为当前生效配置使用

### 4. 检测结果
- 检测结果会生成 InspectionResult
- 可保存 NG 图像
- 可落库到 SQLite
- 可在历史页面查看检测结果

### 5. PLC 联动（可选）
- 支持 PLC 触发检测
- 检测完成后可反馈 OK/NG 结果
- 内置模拟 PLC Server，便于演示

## 项目结构

```text
VisionPlatform/
  App.xaml.cs
  MainWindow.xaml
  Controls/               # 自定义控件
  Infrastructure/         # 服务定位器、转换器
  Models/                 # 数据模型（Recipe、InspectionResult、Defect 等）
  Services/
    Camera/               # 相机管理与相机适配
    Detection/            # 检测器与检测流水线
    Pipeline/             # 检测主流程
    Plc/                  # PLC 通讯与模拟器
    Recipes/              # 配方管理
    Result/               # 结果存储与导出
  ViewModels/             # MVVM 视图模型
  Views/                  # WPF 页面
VisionPlatform.SmokeTest/
  Program.cs              # 烟雾测试入口
```

## 技术栈

- .NET 10 WPF
- OpenCvSharp 4
- CommunityToolkit.Mvvm
- Microsoft.Data.Sqlite

## 环境要求

- Windows 操作系统
- Visual Studio 2026 或 VS Code with C# 扩展
- .NET SDK（项目为 net10.0-windows）

## 运行方式

### 1. 还原依赖

```powershell
dotnet restore
```

### 2. 运行项目

```powershell
dotnet run --project VisionPlatform/VisionPlatform.csproj
```

### 3. 运行烟雾测试

```powershell
dotnet run --project VisionPlatform.SmokeTest/VisionPlatform.SmokeTest.csproj
```

## 使用说明

### 默认体验方式

如果你没有真实相机，可以直接使用“模拟相机”模式：

1. 启动程序
2. 进入“实时检测”页面
3. 选择“模拟相机 (演示)”
4. 打开相机
5. 开始检测

### 配方配置

在“配方管理”页面中可以修改：

- ROI 区域
- 阈值
- 最小/最大面积
- 模板路径
- 触发间隔
- 是否启用各类检测器

## 设计思路

这个项目采用了较清晰的分层设计：

- 采集层：负责输入图像
- 检测层：负责不同类型的视觉判断
- 流水线层：把检测器串成一个完整流程
- 结果层：负责保存、展示和反馈
- UI 层：负责交互与可视化

这使得它比较适合作为视觉算法原型、教学示例或工业视觉平台的基础骨架。

## 后续扩展方向

项目未来可以继续扩展为：

- 接入真实工业相机
- 引入深度学习检测模型
- 增加更复杂的缺陷分类
- 增加多相机并行处理
- 增加更完善的报表与统计分析
- 拓展为完整 MES/产线联调系统

## 说明

当前项目属于“基础视觉检测平台/演示项目”，其核心目标是展示如何把 OpenCV 与 WPF、MVVM、SQLite、PLC 等组件串成一个可运行的视觉处理流程，而不是直接作为成熟工业软件使用。
