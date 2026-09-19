using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using MechKit.Core;

namespace MechKit.Features
{
    internal sealed class PartListOptions
    {
        public PartListOptions()
        {
            Naming = new NamingOptions();
            OnlyMachined = true;
            ExcludeToolbox = true;
            DetectVendorParts = true;
            ExcludeSuppressed = true;
            ReadCustomProperties = true;
        }

        public NamingOptions Naming { get; set; }

        /// <summary>只保留加工件（排除标准件与外购件）。</summary>
        public bool OnlyMachined { get; set; }

        /// <summary>把 Toolbox / SOLIDWORKS Data 目录下的零件视为标准件。</summary>
        public bool ExcludeToolbox { get; set; }

        /// <summary>按厂商/标准件关键词把外购件、标准件识别出来。</summary>
        public bool DetectVendorParts { get; set; }

        public bool ExcludeSuppressed { get; set; }

        /// <summary>打开零件读取自定义属性（图号、材料、来源）。较慢但更准确。</summary>
        public bool ReadCustomProperties { get; set; }
    }

    /// <summary>明细表中的一行（同一零件 + 同一配置合并计数）。</summary>
    internal sealed class PartListRow
    {
        public string PartNumber { get; set; }

        public string Name { get; set; }

        public string Material { get; set; }

        public int Quantity { get; set; }

        /// <summary>加工件 / 标准件 / 外购件 / 虚拟件。</summary>
        public string Classification { get; set; }

        public string FilePath { get; set; }

        public string Configuration { get; set; }

        public string FileName
        {
            get
            {
                if (string.IsNullOrEmpty(FilePath))
                {
                    return string.Empty;
                }

                try
                {
                    return Path.GetFileName(FilePath);
                }
                catch
                {
                    return FilePath;
                }
            }
        }
    }

    /// <summary>
    /// 从装配体统计加工件数量，并解析每个零件的图号与材料。
    /// 遍历逻辑：子装配体递归展开，因此每个实例都会被计入数量。
    /// </summary>
    internal static class PartListService
    {
        private const int DocPart = 1;
        private const int DocAssembly = 2;

        public static List<PartListRow> FromAssembly(ISldWorks swApp, AssemblyDoc assembly,
            PartListOptions options, Action<string> log)
        {
            var context = new CollectContext(swApp, options, log);
            var rows = new List<PartListRow>();

            if (assembly == null)
            {
                return rows;
            }

            var components = assembly.GetComponents(false) as object[];
            if (components == null)
            {
                log("装配体中没有组件。");
                return rows;
            }

            foreach (var item in components)
            {
                VisitComponent(context, item as Component2);
            }

            foreach (var pair in context.Rows)
            {
                var row = pair.Value;
                if (options.OnlyMachined && !string.Equals(row.Classification, "加工件", StringComparison.Ordinal))
                {
                    continue;
                }

                rows.Add(row);
            }

            SortRows(rows);
            if (context.SkippedByPattern > 0)
            {
                log(string.Format("按命名规则排除了 {0} 个组件（视为标准件/焊件的子零件）。",
                    context.SkippedByPattern));
            }

            return rows;
        }

        /// <summary>单个零件/文档的图号与材料。</summary>
        public static PartListRow FromPart(ISldWorks swApp, ModelDoc2 doc, PartListOptions options)
        {
            if (doc == null)
            {
                return null;
            }

            var path = doc.GetPathName();
            var context = new CollectContext(swApp, options, null);
            var properties = GetProperties(context, path);

            var modelMaterial = SwUtils.MaterialName(doc);
            var configuration = SwUtils.ActiveConfigurationName(doc);
            if (NamingOptions.IsPlaceholder(modelMaterial))
            {
                modelMaterial = string.Empty;
            }

            var segmentMaterial = options.Naming.ResolveMaterialFromSegments(path);

            return new PartListRow
            {
                PartNumber = options.Naming.ResolvePartNumber(path, name => Lookup(properties, name)),
                Name = options.Naming.ResolveName(path, name => Lookup(properties, name)),
                Material = !string.IsNullOrEmpty(segmentMaterial)
                    ? segmentMaterial
                    : options.Naming.ResolveMaterial(modelMaterial, name => Lookup(properties, name)),
                Quantity = 1,
                Classification = Classify(context, path, path, properties),
                FilePath = path,
                Configuration = configuration
            };
        }

        /// <summary>把文件夹下的零件逐个汇总（不做数量合并，每个文件计 1）。</summary>
        public static List<PartListRow> FromFolder(ISldWorks swApp, string folder, bool recursive,
            PartListOptions options, Action<string> log)
        {
            var rows = new List<PartListRow>();
            var context = new CollectContext(swApp, options, log);

            foreach (var file in ExportService.CollectFiles(new[] { folder }, recursive, DocPart))
            {
                var properties = GetProperties(context, file);
                var path = file;
                var segmentMaterial = options.Naming.ResolveMaterialFromSegments(path);

                if (!options.Naming.MatchesBomPattern(Path.GetFileName(path)))
                {
                    continue;
                }

                rows.Add(new PartListRow
                {
                    PartNumber = options.Naming.ResolvePartNumber(path, name => Lookup(properties, name)),
                    Name = options.Naming.ResolveName(path, name => Lookup(properties, name)),
                    Material = !string.IsNullOrEmpty(segmentMaterial)
                        ? segmentMaterial
                        : options.Naming.ResolveMaterial(string.Empty, name => Lookup(properties, name)),
                    Quantity = 1,
                    Classification = Classify(context, path, path, properties),
                    FilePath = path,
                    Configuration = string.Empty
                });
            }

            SortRows(rows);
            return rows;
        }

        public static string BuildSummary(IList<PartListRow> rows)
        {
            var machinedKinds = 0;
            var machinedTotal = 0;
            var otherTotal = 0;

            foreach (var row in rows)
            {
                if (string.Equals(row.Classification, "加工件", StringComparison.Ordinal))
                {
                    machinedKinds++;
                    machinedTotal += row.Quantity;
                }
                else
                {
                    otherTotal += row.Quantity;
                }
            }

            return string.Format("加工件 {0} 种 / {1} 件；其他 {2} 件。", machinedKinds, machinedTotal, otherTotal);
        }

        /// <summary>导出 CSV（带 BOM，Excel 直接打开不乱码）。</summary>
        public static string ExportCsv(IList<PartListRow> rows, string folder, string title)
        {
            AppPaths.Ensure(folder);
            var fileName = string.Format("{0}_{1:yyyyMMdd_HHmmss}.csv",
                string.IsNullOrEmpty(title) ? "明细汇总" : title, DateTime.Now);
            var path = Path.Combine(folder, fileName);

            var builder = new StringBuilder();
            builder.AppendLine("序号,图号,名称,材料,数量,类型,配置,文件名");

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                builder.Append(i + 1).Append(',')
                       .Append(Csv(row.PartNumber)).Append(',')
                       .Append(Csv(row.Name)).Append(',')
                       .Append(Csv(row.Material)).Append(',')
                       .Append(row.Quantity).Append(',')
                       .Append(Csv(row.Classification)).Append(',')
                       .Append(Csv(row.Configuration)).Append(',')
                       .Append(Csv(row.FileName)).AppendLine();
            }

            builder.AppendLine();
            builder.AppendLine(Csv(BuildSummary(rows)));

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
            return path;
        }

        /// <summary>把解析出的图号 / 材料写回零件的自定义属性。</summary>
        public static int WriteBack(ISldWorks swApp, IList<PartListRow> rows, string partNumberProperty,
            string materialProperty, bool writePartNumber, bool writeMaterial, Action<string> log)
        {
            var updated = 0;

            foreach (var row in rows)
            {
                if (string.IsNullOrEmpty(row.FilePath) || !File.Exists(row.FilePath))
                {
                    continue;
                }

                ModelDoc2 doc = null;
                var opened = false;

                try
                {
                    doc = SwUtils.FindOpenDocument(swApp, row.FilePath);
                    if (doc == null)
                    {
                        int errors = 0;
                        int warnings = 0;
                        doc = swApp.OpenDoc6(row.FilePath, SwUtils.DocTypeFromPath(row.FilePath),
                            (int)swOpenDocOptions_e.swOpenDocOptions_Silent, string.Empty,
                            ref errors, ref warnings) as ModelDoc2;
                        opened = doc != null;
                    }

                    if (doc == null)
                    {
                        log("无法打开：" + row.FileName);
                        continue;
                    }

                    var ok = true;
                    if (writePartNumber && !string.IsNullOrEmpty(row.PartNumber))
                    {
                        ok &= PropertyService.Write(doc, PropertyService.DocumentLevelConfiguration,
                            new CustomProperty(partNumberProperty, PropertyTypes.Text, row.PartNumber), true);
                    }

                    if (writeMaterial && !string.IsNullOrEmpty(row.Material))
                    {
                        ok &= PropertyService.Write(doc, PropertyService.DocumentLevelConfiguration,
                            new CustomProperty(materialProperty, PropertyTypes.Text, row.Material), true);
                    }

                    if (ok)
                    {
                        int saveErrors = 0;
                        int saveWarnings = 0;
                        doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref saveErrors, ref saveWarnings);
                        updated++;
                        log("✓ 已写回：" + row.FileName);
                    }
                    else
                    {
                        log("✗ 写入失败：" + row.FileName);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("写回属性失败：" + row.FilePath, ex);
                    log("✗ 出错：" + row.FileName + " - " + ex.Message);
                }
                finally
                {
                    if (opened && doc != null)
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

            return updated;
        }

        #region 内部实现

        private sealed class CollectContext
        {
            public CollectContext(ISldWorks app, PartListOptions options, Action<string> log)
            {
                App = app;
                Options = options;
                Log = log ?? delegate { };
                Rows = new Dictionary<string, PartListRow>(StringComparer.OrdinalIgnoreCase);
                Properties = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            }

            public ISldWorks App { get; private set; }

            public PartListOptions Options { get; private set; }

            public Action<string> Log { get; private set; }

            public Dictionary<string, PartListRow> Rows { get; private set; }

            public Dictionary<string, Dictionary<string, string>> Properties { get; private set; }

            /// <summary>因不符合「前缀_日期_材料_名称」被排除的组件数。</summary>
            public int SkippedByPattern { get; set; }
        }

        private static void VisitComponent(CollectContext context, Component2 component)
        {
            if (component == null)
            {
                return;
            }

            try
            {
                if (context.Options.ExcludeSuppressed && component.IsSuppressed())
                {
                    return;
                }

                var docType = component.GetType();
                var path = component.GetPathName() ?? string.Empty;

                if (docType == DocAssembly)
                {
                    // 子装配体：递归展开，其内部零件会按实例数重复计数
                    var children = component.GetChildren() as object[];
                    if (children != null)
                    {
                        foreach (var child in children)
                        {
                            VisitComponent(context, child as Component2);
                        }
                    }

                    return;
                }

                Accumulate(context, component, path);
            }
            catch (Exception ex)
            {
                Log.Warn("遍历组件失败：" + ex.Message);
            }
        }

        private static void Accumulate(CollectContext context, Component2 component, string path)
        {
            var configuration = component.ReferencedConfiguration ?? string.Empty;
            var isVirtual = string.IsNullOrEmpty(path);
            var key = isVirtual
                ? "#virtual|" + (component.Name2 ?? string.Empty)
                : path.ToLowerInvariant() + "|" + configuration.ToLowerInvariant();

            PartListRow row;
            if (context.Rows.TryGetValue(key, out row))
            {
                row.Quantity++;
                return;
            }

            var properties = GetProperties(context, path);

            var modelMaterial = string.Empty;
            try
            {
                modelMaterial = component.GetMaterialUserName();
                if (string.IsNullOrEmpty(modelMaterial))
                {
                    modelMaterial = NamingOptions.NormalizeMaterialId(component.GetMaterialIdName());
                }
            }
            catch (Exception ex)
            {
                Log.Warn("读取组件材料失败：" + ex.Message);
            }

            if (string.IsNullOrEmpty(modelMaterial) && properties != null)
            {
                string material;
                if (properties.TryGetValue("材料", out material) ||
                    properties.TryGetValue("Material", out material))
                {
                    modelMaterial = material;
                }
            }

            var displayPath = isVirtual ? (component.Name2 ?? string.Empty) + ".sldprt" : path;

            // 只收录符合「前缀_日期_材料_名称」的零件；其余是标准件/焊件的子零件，不进 BOM
            var ruleName = component.Name2;
            if (string.IsNullOrEmpty(ruleName))
            {
                ruleName = Path.GetFileName(displayPath);
            }

            if (!context.Options.Naming.MatchesBomPattern(ruleName))
            {
                context.SkippedByPattern++;
                return;
            }

            if (NamingOptions.IsPlaceholder(modelMaterial))
            {
                modelMaterial = string.Empty;
            }

            var segmentMaterial = context.Options.Naming.ResolveMaterialFromSegments(displayPath);

            row = new PartListRow
            {
                PartNumber = context.Options.Naming.ResolvePartNumber(displayPath, name => Lookup(properties, name)),
                Name = context.Options.Naming.ResolveName(displayPath, name => Lookup(properties, name)),
                Material = !string.IsNullOrEmpty(segmentMaterial)
                    ? segmentMaterial
                    : context.Options.Naming.ResolveMaterial(modelMaterial, name => Lookup(properties, name)),
                Quantity = 1,
                Classification = isVirtual ? "虚拟件" : Classify(context, path, ruleName, properties),
                FilePath = path,
                Configuration = configuration
            };

            context.Rows[key] = row;
        }

        private static string Classify(CollectContext context, string path, string name, Dictionary<string, string> properties)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "虚拟件";
            }

            // 命名规则优先：日期开头 = 加工件；已知前缀开头 = 标准件
            var ruleName = NamingOptions.GetFileNameWithoutExtension(
                string.IsNullOrEmpty(name) ? path : name).Trim();

            if (NamingOptions.StartsWithDate(ruleName))
            {
                return "加工件";
            }

            if (context.Options.Naming.HasKnownPrefix(NamingOptions.FirstSegment(ruleName)))
            {
                return "标准件";
            }

            if (context.Options.ExcludeToolbox)
            {
                var lower = path.ToLowerInvariant();
                if (lower.Contains(@"\toolbox\") ||
                    lower.Contains(@"\solidworks data\") ||
                    lower.Contains(@"\browser\"))
                {
                    return "标准件";
                }
            }

            if (context.Options.DetectVendorParts)
            {
                var byKeyword = VendorKeywords.Match(Path.GetFileName(path));
                if (!string.IsNullOrEmpty(byKeyword))
                {
                    return byKeyword;
                }
            }

            if (properties != null)
            {
                var source = First(properties, "来源", "零件类型", "类型", "类别", "Source", "Type");
                if (!string.IsNullOrEmpty(source))
                {
                    var text = source.ToLowerInvariant();
                    if (text.Contains("标准") || text.Contains("standard") || text.Contains("toolbox") ||
                        text.Contains("gb") || text.Contains("国标"))
                    {
                        return "标准件";
                    }

                    if (text.Contains("外购") || text.Contains("外协") || text.Contains("采购") ||
                        text.Contains("purchas") || text.Contains("bought"))
                    {
                        return "外购件";
                    }

                    if (text.Contains("加工") || text.Contains("自制") || text.Contains("machin"))
                    {
                        return "加工件";
                    }
                }
            }

            return "加工件";
        }

        private static Dictionary<string, string> GetProperties(CollectContext context, string path)
        {
            if (!context.Options.ReadCustomProperties ||
                string.IsNullOrEmpty(path) ||
                SwUtils.DocTypeFromPath(path) == 0)
            {
                return null;
            }

            Dictionary<string, string> cached;
            if (context.Properties.TryGetValue(path, out cached))
            {
                return cached;
            }

            cached = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ModelDoc2 doc = null;
            var opened = false;

            try
            {
                doc = SwUtils.FindOpenDocument(context.App, path);
                if (doc == null)
                {
                    int errors = 0;
                    int warnings = 0;
                    doc = context.App.OpenDoc6(path, SwUtils.DocTypeFromPath(path),
                        (int)swOpenDocOptions_e.swOpenDocOptions_Silent, string.Empty,
                        ref errors, ref warnings) as ModelDoc2;
                    opened = doc != null;
                }

                if (doc != null)
                {
                    foreach (var property in PropertyService.Read(doc, PropertyService.DocumentLevelConfiguration))
                    {
                        var value = string.IsNullOrEmpty(property.ResolvedValue) ? property.Value : property.ResolvedValue;
                        cached[property.Name] = value;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("读取零件属性失败：" + Path.GetFileName(path) + " - " + ex.Message);
            }
            finally
            {
                if (opened && doc != null)
                {
                    try
                    {
                        context.App.CloseDoc(doc.GetTitle());
                    }
                    catch
                    {
                        // 忽略关闭异常
                    }
                }

                PropertyService.Release(doc);
            }

            context.Properties[path] = cached;
            return cached;
        }

        private static string Lookup(Dictionary<string, string> properties, string name)
        {
            if (properties == null || string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            string value;
            return properties.TryGetValue(name, out value) ? value : string.Empty;
        }

        private static string First(Dictionary<string, string> properties, params string[] names)
        {
            foreach (var name in names)
            {
                var value = Lookup(properties, name);
                if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(value.Trim()))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static void SortRows(List<PartListRow> rows)
        {
            rows.Sort(delegate(PartListRow left, PartListRow right)
            {
                var byNumber = string.Compare(left.PartNumber, right.PartNumber, StringComparison.OrdinalIgnoreCase);
                return byNumber != 0
                    ? byNumber
                    : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            });
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

        #endregion
    }
}
