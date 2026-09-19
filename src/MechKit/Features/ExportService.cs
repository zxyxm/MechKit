using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using MechKit.Core;

namespace MechKit.Features
{
    /// <summary>一种导出格式及其适用文档类型。</summary>
    internal sealed class ExportFormat
    {
        public ExportFormat(string extension, string displayName, bool part, bool assembly, bool drawing)
        {
            Extension = extension;
            DisplayName = displayName;
            ForPart = part;
            ForAssembly = assembly;
            ForDrawing = drawing;
        }

        public string Extension { get; private set; }

        public string DisplayName { get; private set; }

        public bool ForPart { get; private set; }

        public bool ForAssembly { get; private set; }

        public bool ForDrawing { get; private set; }

        public bool Supports(int docType)
        {
            switch (docType)
            {
                case SwUtils.DocPart:
                    return ForPart;
                case SwUtils.DocAssembly:
                    return ForAssembly;
                case SwUtils.DocDrawing:
                    return ForDrawing;
                default:
                    return false;
            }
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    internal static class ExportFormats
    {
        public static readonly ExportFormat Pdf =
            new ExportFormat(".pdf", "PDF (.pdf)", true, true, true);

        public static readonly ExportFormat Dwg =
            new ExportFormat(".dwg", "DWG (.dwg)", false, false, true);

        public static readonly ExportFormat Dxf =
            new ExportFormat(".dxf", "DXF (.dxf)", false, false, true);

        public static readonly ExportFormat Step =
            new ExportFormat(".step", "STEP (.step)", true, true, false);

        public static readonly ExportFormat Iges =
            new ExportFormat(".igs", "IGES (.igs)", true, true, false);

        public static readonly ExportFormat Stl =
            new ExportFormat(".stl", "STL (.stl)", true, true, false);

        public static readonly ExportFormat[] All = { Pdf, Dwg, Dxf, Step, Iges, Stl };

        public static ExportFormat Find(string extension)
        {
            foreach (var format in All)
            {
                if (string.Equals(format.Extension, extension, StringComparison.OrdinalIgnoreCase))
                {
                    return format;
                }
            }

            return null;
        }
    }

    internal sealed class BatchExportOptions
    {
        public BatchExportOptions()
        {
            Files = new List<string>();
            Formats = new List<ExportFormat>();
            CreateReport = true;
        }

        public List<string> Files { get; private set; }

        public List<ExportFormat> Formats { get; private set; }

        /// <summary>源根目录，用于计算相对路径以保持目录结构。</summary>
        public string SourceRoot { get; set; }

        public string OutputFolder { get; set; }

        public bool KeepTree { get; set; }

        public bool Overwrite { get; set; }

        public bool CreateReport { get; set; }
    }

    internal sealed class BatchExportReport
    {
        public BatchExportReport()
        {
            Details = new List<ExportDetail>();
        }

        public int TotalFiles { get; set; }

        public int Exported { get; set; }

        public int Failed { get; set; }

        public int Skipped { get; set; }

        public bool Cancelled { get; set; }

        public List<ExportDetail> Details { get; private set; }

        public string ReportPath { get; set; }

        public string Summary()
        {
            var builder = new StringBuilder();
            builder.AppendFormat("共处理 {0} 个文件，导出 {1} 个结果。", TotalFiles, Exported);
            if (Failed > 0)
            {
                builder.AppendFormat(" 失败 {0} 个。", Failed);
            }

            if (Skipped > 0)
            {
                builder.AppendFormat(" 跳过 {0} 个。", Skipped);
            }

            if (Cancelled)
            {
                builder.Append(" 任务被取消。");
            }

            return builder.ToString();
        }
    }

    internal sealed class ExportDetail
    {
        public ExportDetail(string source, string target, bool success, string message)
        {
            Source = source;
            Target = target;
            Success = success;
            Message = message;
        }

        public string Source { get; private set; }

        public string Target { get; private set; }

        public bool Success { get; private set; }

        public string Message { get; private set; }
    }

    /// <summary>
    /// 批量导出。SOLIDWORKS API 只能在其主线程调用，
    /// 因此这里全程在 UI 线程执行，由调用方通过 DoEvents + 取消标志保持响应。
    /// </summary>
    internal static class ExportService
    {
        public static BatchExportReport Run(ISldWorks swApp, BatchExportOptions options,
            Action<string> log, Func<bool> isCancelled, Action<int, int> onProgress = null)
        {
            var report = new BatchExportReport();
            report.TotalFiles = options.Files.Count;

            if (options.Formats.Count == 0)
            {
                log("未选择导出格式。");
                return report;
            }

            if (string.IsNullOrEmpty(options.OutputFolder))
            {
                log("未指定输出目录。");
                return report;
            }

            AppPaths.Ensure(options.OutputFolder);

            var index = 0;
            foreach (var file in options.Files)
            {
                index++;
                if (onProgress != null)
                {
                    onProgress(index, options.Files.Count);
                }

                if (isCancelled())
                {
                    report.Cancelled = true;
                    break;
                }

                var docType = SwUtils.DocTypeFromPath(file);
                if (docType == 0)
                {
                    report.Skipped++;
                    report.Details.Add(new ExportDetail(file, string.Empty, false, "不支持的文件类型"));
                    log("跳过（类型不支持）：" + Path.GetFileName(file));
                    continue;
                }

                var formats = new List<ExportFormat>();
                foreach (var format in options.Formats)
                {
                    if (format.Supports(docType))
                    {
                        formats.Add(format);
                    }
                }

                if (formats.Count == 0)
                {
                    report.Skipped++;
                    report.Details.Add(new ExportDetail(file, string.Empty, false, "没有适用于该文档类型的格式"));
                    log(string.Format("跳过（{0} 不支持所选格式）：{1}",
                        SwUtils.DocTypeName(docType), Path.GetFileName(file)));
                    continue;
                }

                var hadFailure = false;
                foreach (var format in formats)
                {
                    if (isCancelled())
                    {
                        report.Cancelled = true;
                        break;
                    }

                    var target = BuildTargetPath(options, file, format);
                    var message = string.Empty;
                    var success = false;

                    try
                    {
                        if (!options.Overwrite && File.Exists(target))
                        {
                            message = "目标文件已存在，已跳过";
                        }
                        else
                        {
                            AppPaths.Ensure(Path.GetDirectoryName(target));
                            success = ExportOne(swApp, file, docType, format, target, out message);
                        }
                    }
                    catch (Exception ex)
                    {
                        message = ex.Message;
                        Log.Error("导出失败：" + file, ex);
                    }

                    report.Details.Add(new ExportDetail(file, target, success, message));

                    if (success)
                    {
                        report.Exported++;
                        log(string.Format("✓ {0} → {1}", Path.GetFileName(file), Path.GetFileName(target)));
                    }
                    else
                    {
                        hadFailure = true;
                        log(string.Format("✗ {0} → {1}：{2}",
                            Path.GetFileName(file), format.DisplayName,
                            string.IsNullOrEmpty(message) ? "导出失败" : message));
                    }
                }

                if (hadFailure)
                {
                    report.Failed++;
                }
            }

            if (options.CreateReport)
            {
                try
                {
                    report.ReportPath = WriteReport(report, options.OutputFolder);
                    log("导出报告：" + report.ReportPath);
                }
                catch (Exception ex)
                {
                    Log.Error("写导出报告失败", ex);
                }
            }

            return report;
        }

        private static bool ExportOne(ISldWorks swApp, string file, int docType, ExportFormat format,
            string target, out string message)
        {
            message = string.Empty;
            ModelDoc2 doc = null;
            var openedHere = false;

            try
            {
                doc = SwUtils.FindOpenDocument(swApp, file);
                if (doc == null)
                {
                    int openErrors = 0;
                    int openWarnings = 0;
                    doc = swApp.OpenDoc6(file, docType,
                        (int)swOpenDocOptions_e.swOpenDocOptions_Silent, string.Empty,
                        ref openErrors, ref openWarnings) as ModelDoc2;
                    openedHere = doc != null;
                }

                if (doc == null)
                {
                    message = "无法打开文档";
                    return false;
                }

                object exportData = null;
                if (format == ExportFormats.Pdf)
                {
                    var pdfData = swApp.GetExportFileData((int)swExportDataFileType_e.swExportPdfData) as IExportPdfData;
                    if (pdfData != null)
                    {
                        pdfData.ViewPdfAfterSaving = false;
                        // 工程图默认导出全部图纸
                        pdfData.SetSheets((int)swExportDataSheetsToExport_e.swExportData_ExportAllSheets, null);
                        exportData = pdfData;
                    }
                }

                int errors = 0;
                int warnings = 0;
                var extension = doc.Extension;
                if (extension == null)
                {
                    message = "无法访问文档扩展接口";
                    return false;
                }

                var ok = extension.SaveAs3(target,
                    (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    exportData, null, ref errors, ref warnings);

                if (!ok)
                {
                    message = DescribeSaveAsError(errors);
                }

                return ok;
            }
            finally
            {
                if (openedHere && doc != null)
                {
                    try
                    {
                        swApp.CloseDoc(doc.GetTitle());
                    }
                    catch
                    {
                        // 忽略关闭异常
                    }
                }

                PropertyService.Release(doc);
            }
        }

        private static string DescribeSaveAsError(int errors)
        {
            if (errors == 0)
            {
                return "SOLIDWORKS 未能保存该文件";
            }

            try
            {
                return string.Format("SOLIDWORKS 返回错误码 {0}（{1}）", errors,
                    Enum.GetName(typeof(swFileSaveError_e), errors) ?? "未知错误");
            }
            catch
            {
                return "SOLIDWORKS 返回错误码 " + errors;
            }
        }

        private static string BuildTargetPath(BatchExportOptions options, string file, ExportFormat format)
        {
            var name = Path.GetFileNameWithoutExtension(file) + format.Extension;
            var directory = options.OutputFolder;

            if (options.KeepTree && !string.IsNullOrEmpty(options.SourceRoot))
            {
                var relative = GetRelativeDirectory(options.SourceRoot, file);
                if (!string.IsNullOrEmpty(relative))
                {
                    directory = Path.Combine(directory, relative);
                }
            }

            return Path.Combine(directory, name);
        }

        private static string GetRelativeDirectory(string root, string file)
        {
            try
            {
                var normalizedRoot = root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                var directory = Path.GetDirectoryName(file);
                if (string.IsNullOrEmpty(directory))
                {
                    return string.Empty;
                }

                if (directory.Length >= normalizedRoot.Length &&
                    directory.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return directory.Substring(normalizedRoot.Length);
                }

                // 不是同一颗子树时退回为文件名所在目录
                return Path.GetFileName(directory);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string WriteReport(BatchExportReport report, string outputFolder)
        {
            var path = Path.Combine(outputFolder,
                string.Format("导出报告-{0:yyyyMMdd-HHmmss}.csv", DateTime.Now));

            var builder = new StringBuilder();
            builder.AppendLine("源文件,目标文件,结果,说明");
            foreach (var detail in report.Details)
            {
                builder.Append(Csv(detail.Source)).Append(',')
                       .Append(Csv(detail.Target)).Append(',')
                       .Append(detail.Success ? "成功" : "失败").Append(',')
                       .Append(Csv(detail.Message)).AppendLine();
            }

            builder.AppendLine();
            builder.AppendLine(Csv(report.Summary()));

            // 带 BOM，Excel 打开中文不乱码
            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
            return path;
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>按扩展名和类型筛选出可导出的文件。</summary>
        public static List<string> CollectFiles(IEnumerable<string> folders, bool recursive, int docTypeFilter)
        {
            var result = new List<string>();
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (var file in Directory.GetFiles(folder, "*.*", option))
                {
                    var docType = SwUtils.DocTypeFromPath(file);
                    if (docType == 0)
                    {
                        continue;
                    }

                    if (docTypeFilter != 0 && docType != docTypeFilter)
                    {
                        continue;
                    }

                    result.Add(file);
                }
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        public static string DescribeCount(int count)
        {
            return string.Format(CultureInfo.CurrentCulture, "{0} 个文件", count);
        }
    }
}
