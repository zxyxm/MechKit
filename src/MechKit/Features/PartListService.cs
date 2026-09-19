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
            AssemblyLevel = -1;
            StandardNameField = "segment:2";
            StandardMaterialField = "tail:3";
            StandardProcessField = "segment:1";
            StandardRemarkField = "property:remark";
            MachinedNameField = "segment:3";
            MachinedMaterialField = "segment:2";
            MachinedProcessField = "property:process";
            MachinedRemarkField = "property:remark";
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

        /// <summary>“位置”列的装配层级：0 仅顶层，正数展开对应级数，-1 显示完整父装配路径。</summary>
        public int AssemblyLevel { get; set; }

        public string StandardNameField { get; set; }
        public string StandardMaterialField { get; set; }
        public string StandardProcessField { get; set; }
        public string StandardRemarkField { get; set; }
        public string MachinedNameField { get; set; }
        public string MachinedMaterialField { get; set; }
        public string MachinedProcessField { get; set; }
        public string MachinedRemarkField { get; set; }
    }

    /// <summary>明细表中的一行（同一零件 + 同一配置合并计数）。</summary>
    internal sealed class PartListRow
    {
        public string Location { get; set; }

        public string PartNumber { get; set; }

        public string Name { get; set; }

        public string Material { get; set; }

        /// <summary>零件自定义属性中的加工工艺，例如车、铣、焊接、表面处理。</summary>
        public string Process { get; set; }

        public string Remark { get; set; }

        public int Quantity { get; set; }

        /// <summary>BOM 属性：加工件 / 标准件。</summary>
        public string Classification { get; set; }

        public string FilePath { get; set; }

        public string Configuration { get; set; }

        public string OriginalName { get; private set; }

        public string OriginalMaterial { get; private set; }

        public string OriginalProcess { get; private set; }

        public string OriginalRemark { get; private set; }

        public bool HasBomEdits
        {
            get
            {
                return !string.Equals(Name ?? string.Empty, OriginalName ?? string.Empty, StringComparison.Ordinal) ||
                       !string.Equals(Material ?? string.Empty, OriginalMaterial ?? string.Empty, StringComparison.Ordinal) ||
                       !string.Equals(Process ?? string.Empty, OriginalProcess ?? string.Empty, StringComparison.Ordinal) ||
                       !string.Equals(Remark ?? string.Empty, OriginalRemark ?? string.Empty, StringComparison.Ordinal);
            }
        }

        public void MarkBomClean()
        {
            OriginalName = Name ?? string.Empty;
            OriginalMaterial = Material ?? string.Empty;
            OriginalProcess = Process ?? string.Empty;
            OriginalRemark = Remark ?? string.Empty;
        }

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

            // 只取顶层组件，再由 VisitComponent 递归。直接取全部组件后再递归会重复计数。
            var components = assembly.GetComponents(true) as object[];
            if (components == null)
            {
                log("装配体中没有组件。");
                return rows;
            }

            var hierarchy = new List<string> { ResolveTopAssemblyName(swApp) };
            foreach (var item in components)
            {
                VisitComponent(context, item as Component2, hierarchy);
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
            var propertyMaterial = First(properties, "材料", "材质", "Material", "材质牌号");

            var row = new PartListRow
            {
                Location = string.Empty,
                PartNumber = options.Naming.ResolvePartNumber(path, name => Lookup(properties, name)),
                Name = options.Naming.ResolveNameFromSegments(path),
                Material = !string.IsNullOrEmpty(segmentMaterial)
                    ? segmentMaterial
                    : !string.IsNullOrEmpty(propertyMaterial)
                    ? propertyMaterial
                    : options.Naming.ResolveMaterial(modelMaterial, name => Lookup(properties, name)),
                Process = First(properties, "工艺", "加工工艺", "制造工艺", "Process"),
                Remark = First(properties, "备注", "说明", "Remark", "Notes"),
                Quantity = 1,
                Classification = Classify(context, path, path, properties),
                FilePath = path,
                Configuration = configuration
            };
            ApplyStandardFields(row, path, options.Naming);
            if (string.IsNullOrEmpty(row.Name))
            {
                row.Name = options.Naming.ResolveName(path, name => Lookup(properties, name));
            }
            ApplyConfiguredFields(row, path, properties, options);

            return row;
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
                var propertyMaterial = First(properties, "材料", "材质", "Material", "材质牌号");

                if (!options.Naming.MatchesBomPattern(Path.GetFileName(path)))
                {
                    continue;
                }

                var row = new PartListRow
                {
                    Location = new DirectoryInfo(folder).Name,
                    PartNumber = options.Naming.ResolvePartNumber(path, name => Lookup(properties, name)),
                    Name = options.Naming.ResolveNameFromSegments(path),
                    Material = !string.IsNullOrEmpty(segmentMaterial)
                        ? segmentMaterial
                        : !string.IsNullOrEmpty(propertyMaterial)
                        ? propertyMaterial
                        : options.Naming.ResolveMaterial(string.Empty, name => Lookup(properties, name)),
                    Process = First(properties, "工艺", "加工工艺", "制造工艺", "Process"),
                    Remark = First(properties, "备注", "说明", "Remark", "Notes"),
                    Quantity = 1,
                    Classification = Classify(context, path, path, properties),
                    FilePath = path,
                    Configuration = string.Empty
                };
                ApplyStandardFields(row, path, options.Naming);
                if (string.IsNullOrEmpty(row.Name))
                {
                    row.Name = options.Naming.ResolveName(path, name => Lookup(properties, name));
                }
                ApplyConfiguredFields(row, path, properties, options);
                rows.Add(row);
            }

            SortRows(rows);
            return rows;
        }

        public static string BuildSummary(IList<PartListRow> rows)
        {
            var machinedKinds = 0;
            var machinedTotal = 0;
            var standardTotal = 0;

            foreach (var row in rows)
            {
                if (string.Equals(row.Classification, "加工件", StringComparison.Ordinal))
                {
                    machinedKinds++;
                    machinedTotal += row.Quantity;
                }
                else
                {
                    standardTotal += row.Quantity;
                }
            }

            return string.Format("加工件 {0} 种 / {1} 件；标准件 {2} 件。", machinedKinds, machinedTotal, standardTotal);
        }

        /// <summary>导出 CSV（带 BOM，Excel 直接打开不乱码）。</summary>
        public static string ExportCsv(IList<PartListRow> rows, string folder, string title)
        {
            AppPaths.Ensure(folder);
            var fileName = string.Format("{0}_{1:yyyyMMdd_HHmmss}.csv",
                string.IsNullOrEmpty(title) ? "明细汇总" : title, DateTime.Now);
            var path = Path.Combine(folder, fileName);

            var builder = new StringBuilder();
            builder.AppendLine("序号,位置,属性,零件名,材料,工艺,数量,备注");

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                builder.Append(i + 1).Append(',')
                       .Append(Csv(row.Location)).Append(',')
                       .Append(Csv(row.Classification)).Append(',')
                       .Append(Csv(row.Name)).Append(',')
                       .Append(Csv(row.Material)).Append(',')
                       .Append(Csv(row.Process)).Append(',')
                       .Append(row.Quantity).Append(',')
                       .Append(Csv(row.Remark)).AppendLine();
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

        /// <summary>
        /// 把 BOM 预览中编辑过的名称、材料、工艺写入零件文档级自定义属性。
        /// 名称只写属性，不重命名文件，避免破坏装配引用。
        /// </summary>
        public static int ApplyBomEdits(ISldWorks swApp, IList<PartListRow> rows, Action<string> log)
        {
            var updated = 0;
            if (swApp == null || rows == null)
            {
                return updated;
            }

            foreach (var row in rows)
            {
                if (row == null || !row.HasBomEdits)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(row.FilePath) || !File.Exists(row.FilePath))
                {
                    log("✗ 无法写入虚拟件或文件不存在：" + (row.FileName ?? row.Name));
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
                        log("✗ 无法打开：" + row.FileName);
                        continue;
                    }

                    var ok = true;
                    if (!string.Equals(row.Name ?? string.Empty, row.OriginalName ?? string.Empty,
                            StringComparison.Ordinal))
                    {
                        ok &= PropertyService.Write(doc, PropertyService.DocumentLevelConfiguration,
                            new CustomProperty("名称", PropertyTypes.Text, row.Name ?? string.Empty), true);
                    }

                    if (!string.Equals(row.Material ?? string.Empty, row.OriginalMaterial ?? string.Empty,
                            StringComparison.Ordinal))
                    {
                        ok &= PropertyService.Write(doc, PropertyService.DocumentLevelConfiguration,
                            new CustomProperty("材料", PropertyTypes.Text, row.Material ?? string.Empty), true);
                    }

                    if (!string.Equals(row.Process ?? string.Empty, row.OriginalProcess ?? string.Empty,
                            StringComparison.Ordinal))
                    {
                        ok &= PropertyService.Write(doc, PropertyService.DocumentLevelConfiguration,
                            new CustomProperty("工艺", PropertyTypes.Text, row.Process ?? string.Empty), true);
                    }

                    if (!string.Equals(row.Remark ?? string.Empty, row.OriginalRemark ?? string.Empty,
                            StringComparison.Ordinal))
                    {
                        ok &= PropertyService.Write(doc, PropertyService.DocumentLevelConfiguration,
                            new CustomProperty("备注", PropertyTypes.Text, row.Remark ?? string.Empty), true);
                    }

                    if (!ok)
                    {
                        log("✗ 写入失败：" + row.FileName);
                        continue;
                    }

                    int saveErrors = 0;
                    int saveWarnings = 0;
                    var saved = doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                        ref saveErrors, ref saveWarnings);
                    if (!saved || saveErrors != 0)
                    {
                        log(string.Format("✗ 保存失败：{0}（错误 {1}）", row.FileName, saveErrors));
                        continue;
                    }

                    row.MarkBomClean();
                    updated++;
                    log("✓ 已应用：" + row.FileName);
                }
                catch (Exception ex)
                {
                    Log.Error("应用 BOM 修改失败：" + row.FilePath, ex);
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

        private static void VisitComponent(CollectContext context, Component2 component, IList<string> hierarchy)
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
                    // 子装配体：把它加入位置路径，再递归其直接子组件。
                    var childHierarchy = new List<string>(hierarchy ?? new string[0]);
                    childHierarchy.Add(ComponentDisplayName(component, path));
                    var children = component.GetChildren() as object[];
                    if (children != null)
                    {
                        foreach (var child in children)
                        {
                            VisitComponent(context, child as Component2, childHierarchy);
                        }
                    }

                    return;
                }

                Accumulate(context, component, path, hierarchy);
            }
            catch (Exception ex)
            {
                Log.Warn("遍历组件失败：" + ex.Message);
            }
        }

        private static void Accumulate(CollectContext context, Component2 component, string path,
            IList<string> hierarchy)
        {
            var configuration = component.ReferencedConfiguration ?? string.Empty;
            var isVirtual = string.IsNullOrEmpty(path);
            var location = FormatLocation(hierarchy, context.Options.AssemblyLevel);
            var key = isVirtual
                ? "#virtual|" + (component.Name2 ?? string.Empty) + "|" + location
                : path.ToLowerInvariant() + "|" + configuration.ToLowerInvariant() + "|" + location;

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
            var propertyMaterial = First(properties, "材料", "材质", "Material", "材质牌号");
            var classification = Classify(context, path, ruleName, properties);

            row = new PartListRow
            {
                Location = location,
                PartNumber = context.Options.Naming.ResolvePartNumber(displayPath, name => Lookup(properties, name)),
                Name = context.Options.Naming.ResolveNameFromSegments(displayPath),
                Material = !string.IsNullOrEmpty(segmentMaterial)
                    ? segmentMaterial
                    : !string.IsNullOrEmpty(propertyMaterial)
                    ? propertyMaterial
                    : context.Options.Naming.ResolveMaterial(modelMaterial, name => Lookup(properties, name)),
                Process = First(properties, "工艺", "加工工艺", "制造工艺", "Process"),
                Remark = First(properties, "备注", "说明", "Remark", "Notes"),
                Quantity = 1,
                Classification = classification,
                FilePath = path,
                Configuration = configuration
            };

            ApplyStandardFields(row, displayPath, context.Options.Naming);
            if (string.IsNullOrEmpty(row.Name))
            {
                row.Name = context.Options.Naming.ResolveName(displayPath, name => Lookup(properties, name));
            }
            ApplyConfiguredFields(row, displayPath, properties, context.Options);

            context.Rows[key] = row;
        }

        private static string ResolveTopAssemblyName(ISldWorks swApp)
        {
            try
            {
                var doc = swApp == null ? null : swApp.IActiveDoc2;
                if (doc != null)
                {
                    var path = doc.GetPathName();
                    var name = NamingOptions.GetFileNameWithoutExtension(
                        string.IsNullOrEmpty(path) ? doc.GetTitle() : path);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name.Trim();
                    }
                }
            }
            catch
            {
                // 未保存文档或离线宿主，使用通用名称。
            }

            return "顶层装配体";
        }

        private static string ComponentDisplayName(Component2 component, string path)
        {
            var value = NamingOptions.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(value) && component != null)
            {
                value = component.Name2 ?? string.Empty;
                var slash = value.LastIndexOf('/');
                if (slash >= 0 && slash < value.Length - 1)
                {
                    value = value.Substring(slash + 1);
                }

                var instance = value.LastIndexOf('<');
                if (instance > 0 && value.EndsWith(">", StringComparison.Ordinal))
                {
                    value = value.Substring(0, instance);
                }
            }

            return string.IsNullOrWhiteSpace(value) ? "子装配体" : value.Trim();
        }

        private static string FormatLocation(IList<string> hierarchy, int assemblyLevel)
        {
            if (hierarchy == null || hierarchy.Count == 0)
            {
                return string.Empty;
            }

            var count = assemblyLevel < 0
                ? hierarchy.Count
                : Math.Min(hierarchy.Count, assemblyLevel + 1);
            var parts = new string[count];
            for (var i = 0; i < count; i++)
            {
                parts[i] = hierarchy[i];
            }

            return string.Join(" > ", parts);
        }

        /// <summary>
        /// 标准件统一按“前缀_中文中间名_原始名称或型号”解释：
        /// 工艺=前缀，零件名=中文中间名，材料/型号=第 3 段及以后。
        /// 例如电气_接近开关_LJ12A3_ZBX 会得到：标准件 / 接近开关 / LJ12A3_ZBX / 电气。
        /// </summary>
        private static void ApplyStandardFields(PartListRow row, string sourceName, NamingOptions naming)
        {
            if (row == null || naming == null ||
                !string.Equals(row.Classification, "标准件", StringComparison.Ordinal))
            {
                return;
            }

            var stem = NamingOptions.GetFileNameWithoutExtension(sourceName).Trim();
            var separator = string.IsNullOrEmpty(naming.SegmentSeparator) ? "_" : naming.SegmentSeparator;
            var parts = stem.Split(new[] { separator }, StringSplitOptions.None);
            if (parts.Length == 0 || !naming.HasKnownPrefix(parts[0]))
            {
                return;
            }

            row.Process = parts[0].Trim();
            row.Name = parts.Length > 1 ? parts[1].Trim() : stem;
            if (parts.Length > 2)
            {
                var originalName = new string[parts.Length - 2];
                Array.Copy(parts, 2, originalName, 0, originalName.Length);
                row.Material = string.Join(separator, originalName).Trim();
            }
            else
            {
                row.Material = string.Empty;
            }
        }

        /// <summary>按 BOM 设置把文件名段或自定义属性映射到表格列。</summary>
        private static void ApplyConfiguredFields(PartListRow row, string sourceName,
            Dictionary<string, string> properties, PartListOptions options)
        {
            if (row == null || options == null)
            {
                return;
            }

            var standard = string.Equals(row.Classification, "标准件", StringComparison.Ordinal);
            row.Name = ResolveConfiguredField(
                standard ? options.StandardNameField : options.MachinedNameField,
                row.Name, sourceName, properties);
            row.Material = ResolveConfiguredField(
                standard ? options.StandardMaterialField : options.MachinedMaterialField,
                row.Material, sourceName, properties);
            row.Process = ResolveConfiguredField(
                standard ? options.StandardProcessField : options.MachinedProcessField,
                row.Process, sourceName, properties);
            row.Remark = ResolveConfiguredField(
                standard ? options.StandardRemarkField : options.MachinedRemarkField,
                row.Remark, sourceName, properties);
        }

        private static string ResolveConfiguredField(string setting, string automaticValue,
            string sourceName, Dictionary<string, string> properties)
        {
            var source = string.IsNullOrWhiteSpace(setting) ? "auto" : setting.Trim().ToLowerInvariant();
            if (source == "auto")
            {
                return automaticValue ?? string.Empty;
            }

            if (source == "empty")
            {
                return string.Empty;
            }

            var stem = NamingOptions.GetFileNameWithoutExtension(sourceName).Trim();
            if (source == "whole")
            {
                return stem;
            }

            if (source.StartsWith("segment:", StringComparison.Ordinal))
            {
                int number;
                if (int.TryParse(source.Substring("segment:".Length), out number) && number > 0)
                {
                    var parts = stem.Split(new[] { '_' }, StringSplitOptions.None);
                    return number <= parts.Length ? parts[number - 1].Trim() : string.Empty;
                }

                return string.Empty;
            }

            if (source.StartsWith("tail:", StringComparison.Ordinal))
            {
                int number;
                if (int.TryParse(source.Substring("tail:".Length), out number) && number > 0)
                {
                    var parts = stem.Split(new[] { '_' }, StringSplitOptions.None);
                    if (number <= parts.Length)
                    {
                        var tail = new string[parts.Length - number + 1];
                        Array.Copy(parts, number - 1, tail, 0, tail.Length);
                        return string.Join("_", tail).Trim();
                    }
                }

                return string.Empty;
            }

            switch (source)
            {
                case "property:name":
                    return First(properties, "名称", "零件名称", "Description", "Title", "Name");
                case "property:material":
                    return First(properties, "材料", "材质", "Material", "材质牌号");
                case "property:process":
                    return First(properties, "工艺", "加工工艺", "制造工艺", "Process");
                case "property:remark":
                    return First(properties, "备注", "说明", "Remark", "Notes");
                default:
                    return automaticValue ?? string.Empty;
            }
        }

        private static string Classify(CollectContext context, string path, string name, Dictionary<string, string> properties)
        {
            // 命名规则优先：日期开头 = 加工件；已知前缀开头 = 标准件
            var ruleName = NamingOptions.GetFileNameWithoutExtension(
                string.IsNullOrEmpty(name) ? path : name).Trim();

            if (context.Options.Naming.IsMachinedName(ruleName))
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
                    return "标准件";
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
                        return "标准件";
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
                var byLocation = string.Compare(left.Location, right.Location, StringComparison.OrdinalIgnoreCase);
                if (byLocation != 0)
                {
                    return byLocation;
                }

                var byType = string.Compare(left.Classification, right.Classification, StringComparison.OrdinalIgnoreCase);
                return byType != 0
                    ? byType
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
