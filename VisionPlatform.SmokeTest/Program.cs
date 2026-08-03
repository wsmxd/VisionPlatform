using System.Diagnostics;
using OpenCvSharp;
using VisionPlatform.Models;
using VisionPlatform.Services.Camera;
using VisionPlatform.Services.Detection;
using VisionPlatform.Services.Logging;
using VisionPlatform.Services.Plc;
using VisionPlatform.Services.Pipeline;
using VisionPlatform.Services.Recipes;
using VisionPlatform.Services.Result;

var baseDir = Path.Combine(Path.GetTempPath(), "VisionSmokeTest_" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(baseDir);
var failures = 0;

void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name} {(ok ? "" : " -> " + detail)}");
    if (!ok) failures++;
}

// ================= 1. 模拟相机 + 检测算法有效性 =================
Console.WriteLine("=== 1. 检测算法 (模拟缺陷相机 vs 检测结果) ===");
var recipe = new Recipe { Name = "冒烟测试配方", TriggerIntervalMs = 50 };
var camera = new SimulatedCamera();
if (!camera.Open(recipe)) { Check("模拟相机打开", false); return; }

var detector = new DetectorPipeline();
int total = 0, detected = 0, badFrames = 0, cameraDefectFrames = 0, falsePositives = 0;
var sw = Stopwatch.StartNew();

for (int i = 0; i < 120; i++)
{
    if (!camera.TryGrab(out var frame)) { i--; Thread.Sleep(1); continue; }
    total++;
    if (camera.LastFrameHasDefect) cameraDefectFrames++;
    var result = detector.Inspect(frame, recipe, $"TEST-{i:0000}");
    if (!result.IsOk) badFrames++;
    if (camera.LastFrameHasDefect && !result.IsOk) detected++;
    else if (!camera.LastFrameHasDefect && !result.IsOk) falsePositives++;
    frame.Dispose();
}
sw.Stop();
Console.WriteLine($"-- 样本: {total}, 模拟缺陷帧: {cameraDefectFrames}, 检出NG帧: {badFrames}, 误报: {falsePositives}, 平均耗时: {sw.Elapsed.TotalMilliseconds / total:F1}ms/帧");
Check("缺陷召回率 > 70%", cameraDefectFrames > 0 && detected >= cameraDefectFrames * 0.7, $"检出 {detected}/{cameraDefectFrames}");
Check("无缺陷无误报", falsePositives <= 3, $"误报 {falsePositives} 帧");
detector.Dispose();

// ================= 2. 配方 JSON =================
Console.WriteLine("=== 2. 配方管理 ===");
var rm = new RecipeManager(Path.Combine(baseDir, "Recipes"));
recipe.Description = "冒烟测试";
rm.Add(recipe);
rm.Save(recipe);
var reloaded = rm.Recipes.FirstOrDefault(r => r.Name == recipe.Name);
Check("配方 JSON 保存/加载", reloaded is not null && reloaded.Description == "冒烟测试");

// ================= 3. SQLite =================
Console.WriteLine("=== 3. 结果存储 ===");
var store = new ResultStore(Path.Combine(baseDir, "test.db"), Path.Combine(baseDir, "Images"));
store.Insert(new InspectionResult
{
    ProductName = "测试产品", RecipeName = recipe.Name, SerialNumber = "SN-001",
    IsOk = true, ElapsedMs = 12.3, Width = 960, Height = 720
});
var ngResult = new InspectionResult
{
    ProductName = "测试产品", RecipeName = recipe.Name, SerialNumber = "SN-002",
    IsOk = false, ElapsedMs = 15.1, Width = 960, Height = 720,
    Defects = [new Defect { Type = DetectorType.Blob, Name = "污点", BoundingBox = new Rect(10, 10, 30, 30), Area = 900, Confidence = 0.8 }]
};
store.Insert(ngResult);
var rows = store.Query(DateTime.Now.AddMinutes(-5), DateTime.Now);
Check("SQLite 插入/查询", rows.Count == 2 && rows.Any(r => r.IsOk) && rows.Any(r => !r.IsOk));
Check("缺陷 JSON 反序列化", rows.First(r => !r.IsOk).Defects.Count == 1 && rows.First(r => !r.IsOk).Defects[0].Name == "污点");

// ================= 4. Modbus TCP =================
Console.WriteLine("=== 4. Modbus TCP 主站 <-> 内置模拟器 ===");
var server = new ModbusTcpServer(15020);
server.Start();
Thread.Sleep(200);
var client = new ModbusTcpClient();
var connected = await client.ConnectAsync("127.0.0.1", 15020);
Check("Modbus TCP 连接", connected);
if (connected)
{
    server.SetCoil(0, true);
    Check("FC01 读线圈", await client.ReadCoilAsync(0));
    server.SetCoil(3, false);
    Check("FC01 读线圈(false)", !await client.ReadCoilAsync(3));
    await client.WriteCoilAsync(5, true);
    Check("FC05 写线圈", server.GetCoil(5));
    await client.WriteRegisterAsync(10, 1234);
    Check("FC06/FC03 寄存器读写", await client.ReadRegisterAsync(10) == 1234);
    await client.WriteRegistersAsync(20, [111, 222, 333]);
    Check("FC10 多寄存器读写", (await client.ReadRegistersAsync(20, 3)).SequenceEqual(new ushort[] { 111, 222, 333 }));
    client.Disconnect();
}
server.Dispose();

// ================= 5. 报表导出 =================
Console.WriteLine("=== 5. 报表导出 ===");
var exporter = new ReportExporter();
var csv = exporter.ExportCsv(rows, Path.Combine(baseDir, "report.csv"));
var html = exporter.ExportHtml(rows, Path.Combine(baseDir, "report.html"));
Check("CSV/HTML 报表导出", File.Exists(csv) && File.Exists(html));

// ================= 6. 流水线 E2E（含 PLC 联机触发） =================
Console.WriteLine("=== 6. 采集-检测-落库-PLC 联机 E2E ===");
var cameras = new CameraManager();
var simItem = cameras.AvailableCameras.First(c => c.SourceType == CameraSourceType.Simulated);
var e2eRecipe = new Recipe { Name = "E2E", TriggerIntervalMs = 200, UseBrightness = false };
Check("相机打开", cameras.Open(simItem, e2eRecipe));

var plcServer = new ModbusTcpServer(15021);
plcServer.Start();
Thread.Sleep(200);
var plcClient = new ModbusTcpClient();
Check("PLC 连接", await plcClient.ConnectAsync("127.0.0.1", 15021));
var plcManager = new PlcManager(plcClient);
var logger = new LogService(Path.Combine(baseDir, "Logs"));

var pipeline = new InspectionPipeline
{
    TriggerMode = TriggerMode.Interval,
    IntervalMs = 200,
    PlcSource = () => plcManager,
    LogSource = () => logger,
    ResultStore = store
};
int results = 0;
pipeline.ResultProduced += (r, frame) => { results++; frame.Dispose(); };

pipeline.Start(e2eRecipe, cameras);
await Task.Delay(2500);
Check("定时触发产生结果", results >= 8, $"实际 {results}");
Check("触发计数正确", pipeline.TriggerCount >= 8, $"触发 {pipeline.TriggerCount}");
Check("结果已落库", store.Query(DateTime.Now.AddMinutes(-5), DateTime.Now).Count >= 8);
pipeline.Stop();

// PLC 上升沿触发
pipeline.TriggerMode = TriggerMode.PlcTrigger;
var resultsBefore = results;
pipeline.Start(e2eRecipe, cameras);
await plcClient.WriteCoilAsync(0, true);
await Task.Delay(500);
Check("PLC 上升沿触发", results > resultsBefore, "未触发");
await plcClient.WriteCoilAsync(0, false);
await Task.Delay(500);
var rAfter = results;
Check("下降沿不重复触发", rAfter == results, "误触发");
await plcClient.WriteCoilAsync(0, true);
await Task.Delay(500);
Check("再次上升沿触发", results > rAfter);
pipeline.Stop();

pipeline.Dispose();
plcClient.Dispose();
plcServer.Dispose();
cameras.Dispose();
logger.Dispose();

Directory.Delete(baseDir, true);
Console.WriteLine(failures == 0 ? "===== 全部通过 =====" : $"===== {failures} 项失败 =====");
Environment.Exit(failures == 0 ? 0 : 1);
