using System.IO;
using System.Text;
using VisionPlatform.Models;

namespace VisionPlatform.Services.Result;

/// <summary>报表导出：CSV 明细 / HTML 统计报表。</summary>
public sealed class ReportExporter
{
    public string ExportCsv(IReadOnlyList<InspectionResult> results, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("时间,产品,序列号,配方,结果,耗时(ms),缺陷数,缺陷明细");
        foreach (var r in results)
        {
            var defects = string.Join("; ", r.Defects.Select(d => $"{d.Name}({d.Detail})"));
            sb.AppendLine($"\"{r.Timestamp:yyyy-MM-dd HH:mm:ss.fff}\",\"{r.ProductName}\",\"{r.SerialNumber}\",\"{r.RecipeName}\",{(r.IsOk ? "OK" : "NG")},{r.ElapsedMs:F1},{r.Defects.Count},\"{defects}\"");
        }
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        return path;
    }

    public string ExportHtml(IReadOnlyList<InspectionResult> results, string path)
    {
        var ok = results.Count(r => r.IsOk);
        var ng = results.Count - ok;
        var total = results.Count;
        var yield = total > 0 ? (double)ok / total * 100 : 0;

        var rows = new StringBuilder();
        foreach (var r in results)
        {
            var defects = string.Join("<br/>", r.Defects.Select(d => $"<span style='color:#c0392b'>{d.Name}</span> {d.Detail}")) ?? "-";
            rows.AppendLine($"""
                <tr>
                    <td>{r.Timestamp:yyyy-MM-dd HH:mm:ss}</td>
                    <td>{r.ProductName}</td>
                    <td>{r.SerialNumber}</td>
                    <td class="{r.IsOk.ToString().ToLower()}">{(r.IsOk ? "OK" : "NG")}</td>
                    <td>{r.ElapsedMs:F1}</td>
                    <td>{defects}</td>
                </tr>
                """);
        }

        var html = $$"""
        <!DOCTYPE html>
        <html lang="zh-CN">
        <head>
        <meta charset="utf-8"/>
        <title>VisionPlatform 检测报表</title>
        <style>
            body { font-family: "Microsoft YaHei", sans-serif; background:#1e2229; color:#e8eaed; margin:24px; }
            h1 { font-size: 20px; border-bottom: 2px solid #3e9bff; padding-bottom: 8px; }
            .cards { display:flex; gap:16px; margin:16px 0; }
            .card { background:#242932; border:1px solid #333a46; border-radius:6px; padding:16px 28px; }
            .card .num { font-size: 28px; font-weight: 600; }
            .card .lbl { color:#98a1ae; font-size:12px; margin-top:4px; }
            .ok { color:#34c759; font-weight:600; } .ng { color:#ff453a; font-weight:600; }
            table { width:100%; border-collapse:collapse; margin-top:16px; font-size:13px; }
            th, td { border:1px solid #333a46; padding:6px 10px; text-align:left; }
            th { background:#242932; }
            tr:nth-child(even) { background:#21262e; }
            .info { color:#98a1ae; font-size:12px; }
        </style>
        </head>
        <body>
            <h1>VisionPlatform 检测报表</h1>
            <div class="info">生成时间: {{DateTime.Now:yyyy-MM-dd HH:mm:ss}} &nbsp;|&nbsp; 记录数: {{total}}</div>
            <div class="cards">
                <div class="card"><div class="num">{{total}}</div><div class="lbl">总检测数</div></div>
                <div class="card"><div class="num ok">{{ok}}</div><div class="lbl">良品 (OK)</div></div>
                <div class="card"><div class="num ng">{{ng}}</div><div class="lbl">不良品 (NG)</div></div>
                <div class="card"><div class="num">{{yield:F2}}%</div><div class="lbl">良品率</div></div>
            </div>
            <table>
                <thead><tr><th>时间</th><th>产品</th><th>序列号</th><th>结果</th><th>耗时(ms)</th><th>缺陷</th></tr></thead>
                <tbody>
                    {{rows}}
                </tbody>
            </table>
        </body>
        </html>
        """;
        File.WriteAllText(path, html, Encoding.UTF8);
        return path;
    }
}
