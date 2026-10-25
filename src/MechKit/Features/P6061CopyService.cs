using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MechKit.Features
{
    internal static class P6061CopyService
    {
        internal static bool Matches(string path)
        {
            if (!string.Equals(Path.GetExtension(path), ".sldprt", StringComparison.OrdinalIgnoreCase)) return false;
            var fields = Path.GetFileNameWithoutExtension(path).Split('-', '_');
            return fields.Length > 1 && string.Equals(fields[1], "p6061", StringComparison.OrdinalIgnoreCase);
        }

        internal static string Copy(IEnumerable<string> sources, string destination)
        {
            Directory.CreateDirectory(destination);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var report = new StringBuilder("p6061 零件及同名工程图复制报告\r\n复制的是磁盘已保存文件。工程图内部引用未改写。\r\n");
            int parts = 0, drawings = 0, skipped = 0, missing = 0, failed = 0;
            foreach (var source in sources)
            {
                if (!Matches(source) || !files.Add(Path.GetFullPath(source))) continue;
                var drawing = Path.ChangeExtension(source, ".slddrw");
                if (File.Exists(drawing)) files.Add(Path.GetFullPath(drawing));
                else { missing++; report.AppendLine("缺少同名工程图：" + source); }
            }
            // Reject all members of a name collision, rather than silently choosing one.
            var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (string.Equals(Path.GetExtension(file), ".sldprt", StringComparison.OrdinalIgnoreCase))
                    names[name] = names.ContainsKey(name) ? names[name] + 1 : 1;
            }
            foreach (var source in files)
            {
                var target = Path.Combine(destination, Path.GetFileName(source));
                if (names[Path.GetFileNameWithoutExtension(source)] > 1 || File.Exists(target))
                {
                    skipped++; report.AppendLine("跳过（同名冲突或目标已存在）：" + source); continue;
                }
                try
                {
                    File.Copy(source, target, false);
                    if (Matches(source)) parts++; else drawings++;
                    report.AppendLine("已复制：" + source + " → " + target);
                }
                catch (Exception ex) { failed++; report.AppendLine("复制失败：" + source + "；" + ex.Message); }
            }
            var summary = string.Format("已复制零件 {0} 个、工程图 {1} 个；缺图 {2} 个、跳过 {3} 个文件、失败 {4} 个文件。", parts, drawings, missing, skipped, failed);
            report.AppendLine(summary);
            var reportPath = Path.Combine(destination, "p6061复制报告-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
            try { File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8); }
            catch (Exception ex) { return summary + "\r\n报告写入失败：" + ex.Message; }
            return summary + "\r\n报告：" + reportPath;
        }
    }
}
