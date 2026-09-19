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
            StandardNameField = "tail:3";
            StandardMaterialField = "empty";
            StandardProcessField = "segment:2";
            StandardRemarkField = "auto";
            MachinedNameField = "auto";
            MachinedMaterialField = "auto";
            MachinedProcessField = "auto";
            MachinedRemarkField = "property:remark";
            SequenceHeader = "序号";
            LocationHeader = "位置";
            FullNameHeader = "完整名称";
            DrawingHeader = "二维工程图";
            ClassificationHeader = "属性";
            NameHeader = "零件名称/标准件名称";
            MaterialHeader = "材料/型号";
            ProcessHeader = "工艺/渠道";
            SurfaceHeader = "表面处理";
            QuantityHeader = "数量";
            AssemblyNoteHeader = "安装说明";
            RemarkHeader = "备注";
            ColumnOrder = PartListService.DefaultColumnOrder;
            StandardAssemblyNoteField = "auto";
            MachinedAssemblyNoteField = "rule:assemblynote";
            StandardSurfaceField = "empty";
            MachinedSurfaceField = "rule:surface";
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

        /// <summary>“位置”列的组织层级：0 总装，1 总装/部装，-1 显示完整父装配路径。</summary>
        public int AssemblyLevel { get; set; }

        public string StandardNameField { get; set; }
        public string StandardMaterialField { get; set; }
        public string StandardProcessField { get; set; }
        public string StandardRemarkField { get; set; }
        public string MachinedNameField { get; set; }
        public string MachinedMaterialField { get; set; }
        public string MachinedProcessField { get; set; }
        public string MachinedRemarkField { get; set; }
        public string SequenceHeader { get; set; }
        public string LocationHeader { get; set; }
        public string FullNameHeader { get; set; }
        public string DrawingHeader { get; set; }
        public string ClassificationHeader { get; set; }
        public string NameHeader { get; set; }
        public string MaterialHeader { get; set; }
        public string ProcessHeader { get; set; }
        public string SurfaceHeader { get; set; }
        public string QuantityHeader { get; set; }
        public string AssemblyNoteHeader { get; set; }
        public string RemarkHeader { get; set; }
        public string ColumnOrder { get; set; }
        public string StandardAssemblyNoteField { get; set; }
        public string MachinedAssemblyNoteField { get; set; }
        public string StandardSurfaceField { get; set; }
        public string MachinedSurfaceField { get; set; }
    }

    /// <summary>明细表中的一行（同一零件 + 同一配置合并计数）。</summary>
    internal sealed class PartListRow
    {
        public string AssemblyNote { get; set; }

        public string Location { get; set; }

        /// <summary>不拆分、不改写的完整零部件名称（不含文件扩展名和实例序号）。</summary>
        public string FullName { get; set; }

        /// <summary>对应的二维工程图文件（同目录同名 .slddrw）；没有工程图时为空。</summary>
        public string DrawingPath { get; set; }

        public string PartNumber { get; set; }

        public string Name { get; set; }

        public string Material { get; set; }

        /// <summary>零件自定义属性中的加工工艺，例如车、铣、焊接、表面处理。</summary>
        public string Process { get; set; }

        public string SurfaceTreatment { get; set; }

        public string Remark { get; set; }

        public int Quantity { get; set; }

        /// <summary>BOM 属性：加工件 / 标准件。</summary>
        public string Classification { get; set; }

        /// <summary>
        /// 不符合「加工件 / 标准件 / 参考件」命名的组件（含被忽略的子装配体）：
        /// 排在表格最后并标红，提醒补齐命名规则。
        /// </summary>
        public bool IsUnmatched { get; set; }

        public string FilePath { get; set; }

        public string Configuration { get; set; }

        public string OriginalName { get; private set; }

        /// <summary>“三维名称”的原始值，用来判断用户是否直接改过名字。</summary>
        public string OriginalFullName { get; private set; }

        public string OriginalMaterial { get; private set; }

        public string OriginalProcess { get; private set; }

        public string OriginalRemark { get; private set; }

        public bool HasBomEdits
        {
            get
            {
                return HasNamingEdits ||
                       !string.Equals(Remark ?? string.Empty, OriginalRemark ?? string.Empty, StringComparison.Ordinal);
            }
        }

        public bool HasNamingEdits
        {
            get
            {
                return !string.Equals(Name ?? string.Empty, OriginalName ?? string.Empty, StringComparison.Ordinal) ||
                       !string.Equals(Material ?? string.Empty, OriginalMaterial ?? string.Empty, StringComparison.Ordinal) ||
                       !string.Equals(Process ?? string.Empty, OriginalProcess ?? string.Empty, StringComparison.Ordinal);
            }
        }

        /// <summary>用户是否直接改了「三维名称」列（改完按这个名字重命名文件）。</summary>
        public bool HasFullNameEdit
        {
            get
            {
                return !string.Equals(FullName ?? string.Empty, OriginalFullName ?? string.Empty,
                    StringComparison.Ordinal);
            }
        }

        public void MarkBomClean()
        {
            MarkNamingClean();
            OriginalRemark = Remark ?? string.Empty;
        }

        public void MarkNamingClean()
        {
            OriginalName = Name ?? string.Empty;
            OriginalFullName = FullName ?? string.Empty;
            OriginalMaterial = Material ?? string.Empty;
            OriginalProcess = Process ?? string.Empty;
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

    /// <summary>当前装配体中可作为独立 BOM 范围的子装配体。</summary>
    internal sealed class SubassemblyScope
    {
        public Component2 Component { get; set; }

        public string DisplayName { get; set; }

        public override string ToString()
        {
            return DisplayName ?? string.Empty;
        }
    }

    /// <summary>子装配体在 BOM 中的处理方式。</summary>
    internal enum SubassemblyAction
    {
        /// <summary>按加工件命名（日期开头）的子装配体：继续往下读取子零件。</summary>
        Expand,

        /// <summary>以标准件前缀开头的子装配体：整机外购，作为一条标准件计入。</summary>
        TreatAsStandard,

        /// <summary>不符合命名规则的子装配体：整层忽略。</summary>
        Ignore
    }

    /// <summary>单个组件在 BOM 表格里的处理方式。</summary>
    internal enum RowDisposition
    {
        /// <summary>符合加工件 / 标准件命名：正常进表。</summary>
        Matched,

        /// <summary>参考件（参考- 开头）：明确不进 BOM，也不提示。</summary>
        Reference,

        /// <summary>不符合任何命名规则：排到表格最后一行区并标红。</summary>
        Unmatched
    }

    /// <summary>
    /// 从装配体统计加工件数量，并解析每个零件的图号与材料。
    /// 遍历逻辑：子装配体递归展开，因此每个实例都会被计入数量。
    /// </summary>
        internal static class PartListService
    {
        public const string DefaultColumnOrder =
            "sequence,location,fullname,drawing,classification,name,material,process,surface,quantity,assemblynote,remark";

        /// <summary>不符合命名规则的组件在“属性”列里的显示名（表格最后一行区、标红）。</summary>
        internal const string UnmatchedClassification = "未匹配";

        private static readonly string[] AllColumnKeys = DefaultColumnOrder.Split(',');
        private const int DocPart = 1;
        private const int DocAssembly = 2;

        public static string[] ParseColumnOrder(string value)
        {
            var result = new List<string>();
            foreach (var token in (value ?? string.Empty).Split(','))
            {
                var key = token.Trim().ToLowerInvariant();
                if (Array.IndexOf(AllColumnKeys, key) >= 0 && !result.Contains(key))
                {
                    result.Add(key);
                }
            }
            foreach (var key in AllColumnKeys)
            {
                if (!result.Contains(key))
                {
                    result.Add(key);
                }
            }
            return result.ToArray();
        }

        public static string SerializeColumnOrder(IEnumerable<string> columns)
        {
            return string.Join(",", ParseColumnOrder(columns == null
                ? string.Empty
                : string.Join(",", new List<string>(columns).ToArray())));
        }

        /// <summary>从同一份持久化设置创建 BOM 解析、表头和导出配置。</summary>
        public static PartListOptions CreateOptions(AddinSettings settings)
        {
            settings = settings ?? new AddinSettings();
            return new PartListOptions
            {
                Naming = NamingOptionsFactory.FromSettings(settings),
                OnlyMachined = settings.PartListOnlyMachined,
                DetectVendorParts = settings.DetectVendorParts,
                ReadCustomProperties = settings.PartListReadProperties,
                AssemblyLevel = settings.BomAssemblyLevel,
                StandardNameField = settings.BomStandardNameField,
                StandardMaterialField = settings.BomStandardMaterialField,
                StandardProcessField = settings.BomStandardProcessField,
                StandardRemarkField = settings.BomStandardRemarkField,
                MachinedNameField = settings.BomMachinedNameField,
                MachinedMaterialField = settings.BomMachinedMaterialField,
                MachinedProcessField = settings.BomMachinedProcessField,
                MachinedRemarkField = settings.BomMachinedRemarkField,
                SequenceHeader = settings.BomSequenceHeader,
                LocationHeader = settings.BomLocationHeader,
                FullNameHeader = settings.BomFullNameHeader,
                DrawingHeader = settings.BomDrawingHeader,
                ClassificationHeader = settings.BomClassificationHeader,
                NameHeader = settings.BomNameHeader,
                MaterialHeader = settings.BomMaterialHeader,
                ProcessHeader = settings.BomProcessHeader,
                SurfaceHeader = settings.BomSurfaceHeader,
                QuantityHeader = settings.BomQuantityHeader,
                AssemblyNoteHeader = settings.BomAssemblyNoteHeader,
                RemarkHeader = settings.BomRemarkHeader,
                ColumnOrder = settings.BomColumnOrder,
                StandardAssemblyNoteField = settings.BomStandardAssemblyNoteField,
                MachinedAssemblyNoteField = settings.BomMachinedAssemblyNoteField,
                StandardSurfaceField = settings.BomStandardSurfaceField,
                MachinedSurfaceField = settings.BomMachinedSurfaceField
            };
        }

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

            return FinalizeRows(context, options);
        }

        /// <summary>递归读取当前装配体下的全部子装配体，保留实例层级供用户选择。</summary>
        public static List<SubassemblyScope> GetSubassemblies(AssemblyDoc assembly)
        {
            var result = new List<SubassemblyScope>();
            if (assembly == null)
            {
                return result;
            }

            try
            {
                var components = assembly.GetComponents(true) as object[];
                if (components == null)
                {
                    return result;
                }

                foreach (var item in components)
                {
                    CollectSubassemblies(item as Component2, new List<string>(), result);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("读取子装配体失败：" + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// 只统计用户在装配树或图形区选中的零件/子装配体。
        /// 子装配体从其直接子组件开始递归，因此数量相对于该部件重新统计。
        /// </summary>
        public static List<PartListRow> FromComponent(ISldWorks swApp, Component2 root,
            PartListOptions options, Action<string> log)
        {
            var context = new CollectContext(swApp, options, log);
            if (root == null)
            {
                return new List<PartListRow>();
            }

            try
            {
                var path = root.GetPathName() ?? string.Empty;
                var hierarchy = new List<string> { ComponentDisplayName(root, path) };
                if (root.GetType() == DocAssembly)
                {
                    var children = root.GetChildren() as object[];
                    if (children != null)
                    {
                        foreach (var child in children)
                        {
                            VisitComponent(context, child as Component2, hierarchy);
                        }
                    }
                }
                else
                {
                    Accumulate(context, root, path, hierarchy);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("统计选中部件失败：" + ex.Message);
                context.Log("统计选中部件失败：" + ex.Message);
            }

            return FinalizeRows(context, options);
        }

        public static string DisplayNameForComponent(Component2 component)
        {
            if (component == null)
            {
                return string.Empty;
            }

            try
            {
                return ComponentDisplayName(component, component.GetPathName() ?? string.Empty);
            }
            catch
            {
                return component.Name2 ?? string.Empty;
            }
        }

        private static void CollectSubassemblies(Component2 component, IList<string> parents,
            ICollection<SubassemblyScope> result)
        {
            if (component == null)
            {
                return;
            }

            try
            {
                if (component.GetType() != DocAssembly)
                {
                    return;
                }

                var displayName = DisplayNameForComponent(component);
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = component.Name2 ?? "子装配体";
                }

                var hierarchy = new List<string>(parents ?? new string[0]);
                hierarchy.Add(displayName);
                result.Add(new SubassemblyScope
                {
                    Component = component,
                    DisplayName = string.Join(" > ", hierarchy.ToArray())
                });

                var children = component.GetChildren() as object[];
                if (children == null)
                {
                    return;
                }

                foreach (var child in children)
                {
                    CollectSubassemblies(child as Component2, hierarchy, result);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("读取子装配体节点失败：" + ex.Message);
            }
        }

        private static List<PartListRow> FinalizeRows(CollectContext context,
            PartListOptions options)
        {
            var rows = new List<PartListRow>();
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
                context.Log(string.Format(
                    "{0} 个组件不符合命名规则，已排在表格末尾并标红（属性列显示“未匹配”）。",
                    context.SkippedByPattern));
            }

            if (context.SkippedAssemblies > 0)
            {
                context.Log(string.Format(
                    "{0} 个子装配体未按「日期-装配」命名：内部零件不参与统计，装配体本身排在表格末尾并标红。",
                    context.SkippedAssemblies));
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
                FullName = ResolveFullName(path),
                DrawingPath = FindDrawingFor(path),
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
            ApplyMachinedFields(row, path, options.Naming);
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
                    FullName = ResolveFullName(path),
                    DrawingPath = FindDrawingFor(path),
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
                ApplyMachinedFields(row, path, options.Naming);
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
            var referenceTotal = 0;
            var unmatchedTotal = 0;

            foreach (var row in rows)
            {
                if (string.Equals(row.Classification, "加工件", StringComparison.Ordinal))
                {
                    machinedKinds++;
                    machinedTotal += row.Quantity;
                }
                else if (string.Equals(row.Classification, "参考件", StringComparison.Ordinal))
                {
                    referenceTotal += row.Quantity;
                }
                else if (string.Equals(row.Classification, UnmatchedClassification, StringComparison.Ordinal))
                {
                    unmatchedTotal += row.Quantity;
                }
                else
                {
                    standardTotal += row.Quantity;
                }
            }

            var summary = string.Format("加工件 {0} 种 / {1} 件；标准件 {2} 件；参考件 {3} 件。",
                machinedKinds, machinedTotal, standardTotal, referenceTotal);
            if (unmatchedTotal > 0)
            {
                summary += string.Format("未匹配命名规则 {0} 件（见表格末尾标红行）。", unmatchedTotal);
            }

            return summary;
        }

        /// <summary>导出 CSV（带 BOM，Excel 直接打开不乱码）。</summary>
        public static string ExportCsv(IList<PartListRow> rows, string folder, string title,
            PartListOptions options)
        {
            options = options ?? new PartListOptions();
            AppPaths.Ensure(folder);
            var fileName = string.Format("{0}-{1:yyyyMMdd-HHmmss}.csv",
                string.IsNullOrEmpty(title) ? "明细汇总" : title, DateTime.Now);
            var path = Path.Combine(folder, fileName);

            var builder = new StringBuilder();
            var columns = ParseColumnOrder(options.ColumnOrder);
            for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
            {
                if (columnIndex > 0) builder.Append(',');
                builder.Append(Csv(ColumnHeader(options, columns[columnIndex])));
            }
            builder.AppendLine();

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
                {
                    if (columnIndex > 0) builder.Append(',');
                    builder.Append(Csv(ColumnValue(row, i + 1, columns[columnIndex])));
                }
                builder.AppendLine();
            }

            builder.AppendLine();
            builder.AppendLine(Csv(BuildSummary(rows)));

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
            return path;
        }

        private static string Header(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        public static string ColumnHeader(PartListOptions options, string key)
        {
            options = options ?? new PartListOptions();
            switch ((key ?? string.Empty).ToLowerInvariant())
            {
                case "sequence": return Header(options.SequenceHeader, "序号");
                case "location": return Header(options.LocationHeader, "位置");
                case "fullname": return Header(options.FullNameHeader, "完整名称");
                case "drawing": return Header(options.DrawingHeader, "二维工程图");
                case "classification": return Header(options.ClassificationHeader, "属性");
                case "name": return Header(options.NameHeader, "零件名称/标准件名称");
                case "material": return Header(options.MaterialHeader, "材料/型号");
                case "process": return Header(options.ProcessHeader, "工艺/渠道");
                case "surface": return Header(options.SurfaceHeader, "表面处理");
                case "quantity": return Header(options.QuantityHeader, "数量");
                case "assemblynote": return Header(options.AssemblyNoteHeader, "安装说明");
                case "remark": return Header(options.RemarkHeader, "备注");
                default: return key ?? string.Empty;
            }
        }

        private static string ColumnValue(PartListRow row, int sequence, string key)
        {
            if (row == null) return string.Empty;
            switch ((key ?? string.Empty).ToLowerInvariant())
            {
                case "sequence": return sequence.ToString();
                case "location": return row.Location;
                case "fullname": return row.FullName;
                case "drawing":
                    return string.IsNullOrEmpty(row.DrawingPath)
                        ? string.Empty
                        : Path.GetFileName(row.DrawingPath);
                case "classification": return row.Classification;
                case "name": return row.Name;
                case "material": return row.Material;
                case "process": return row.Process;
                case "surface": return row.SurfaceTreatment;
                case "quantity": return row.Quantity.ToString();
                case "assemblynote": return row.AssemblyNote;
                case "remark": return row.Remark;
                default: return string.Empty;
            }
        }

        /// <summary>
        /// 双击 BOM 行时打开对应文档：优先同目录同名的工程图（零件图），
        /// 没有工程图时打开零件 / 装配体模型。返回实际打开的路径，失败返回空字符串。
        /// </summary>
        public static string OpenRowDocument(ISldWorks swApp, PartListRow row, Action<string> log)
        {
            log = log ?? delegate { };
            if (swApp == null || row == null)
            {
                return string.Empty;
            }

            var modelPath = row.FilePath;
            if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
            {
                log("✗ 这一行没有对应的零件文件（虚拟件或未保存的零件打不开）。");
                return string.Empty;
            }

            var drawingPath = FindDrawingFor(modelPath);
            var target = string.IsNullOrEmpty(drawingPath) ? modelPath : drawingPath;

            try
            {
                var opened = SwUtils.FindOpenDocument(swApp, target);
                if (opened != null)
                {
                    ActivateDocument(swApp, opened);
                    log("已切换到：" + Path.GetFileName(target));
                    return target;
                }

                int errors = 0;
                int warnings = 0;
                var doc = swApp.OpenDoc6(target, SwUtils.DocTypeFromPath(target),
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent, string.Empty, ref errors, ref warnings);
                if (doc == null)
                {
                    log(string.Format("✗ 打开失败（错误码 {0}）：{1}", errors, Path.GetFileName(target)));
                    return string.Empty;
                }

                log(string.Format("已打开{0}：{1}",
                    SwUtils.DocTypeName(SwUtils.DocTypeFromPath(target)), Path.GetFileName(target)));
                return target;
            }
            catch (Exception ex)
            {
                log("✗ 打开文档失败：" + ex.Message);
                Log.Warn("双击打开零件图失败：" + ex.Message);
                return string.Empty;
            }
        }

        /// <summary>同目录下与零件 / 装配体同名的工程图（.slddrw）；没有则返回空字符串。</summary>
        /// <summary>
        /// 打开这一行对应的三维模型（.sldprt / .sldasm）：优先用当前文档里已打开的，
        /// 否则按路径打开。返回实际打开的路径，失败返回空字符串。
        /// </summary>
        public static string OpenRowModelDocument(ISldWorks swApp, PartListRow row, Action<string> log)
        {
            log = log ?? delegate { };
            if (swApp == null || row == null || string.IsNullOrEmpty(row.FilePath) ||
                !File.Exists(row.FilePath))
            {
                log("✗ 这一行没有对应的零件 / 装配体文件（虚拟件或未保存的零件打不开）。");
                return string.Empty;
            }

            var target = row.FilePath;
            try
            {
                var opened = SwUtils.FindOpenDocument(swApp, target);
                if (opened != null)
                {
                    ActivateDocument(swApp, opened);
                    log("已切换到：" + Path.GetFileName(target));
                    return target;
                }

                int errors = 0;
                int warnings = 0;
                var doc = swApp.OpenDoc6(target, SwUtils.DocTypeFromPath(target),
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent, string.Empty, ref errors, ref warnings);
                if (doc == null)
                {
                    log(string.Format("✗ 打开失败（错误码 {0}）：{1}", errors, Path.GetFileName(target)));
                    return string.Empty;
                }

                log(string.Format("已打开{0}：{1}",
                    SwUtils.DocTypeName(SwUtils.DocTypeFromPath(target)), Path.GetFileName(target)));
                return target;
            }
            catch (Exception ex)
            {
                log("✗ 打开零件 / 装配体失败：" + ex.Message);
                Log.Warn("双击打开三维模型失败：" + ex.Message);
                return string.Empty;
            }
        }

        /// <summary>同目录下与零件 / 装配体同名的工程图（.slddrw）；没有则返回空字符串。</summary>
        internal static string FindDrawingFor(string modelPath)
        {
            try
            {
                var directory = Path.GetDirectoryName(modelPath);
                var stem = Path.GetFileNameWithoutExtension(modelPath);
                if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(stem) ||
                    !Directory.Exists(directory))
                {
                    return string.Empty;
                }

                foreach (var candidate in Directory.GetFiles(directory, stem + ".slddrw"))
                {
                    return candidate;
                }

                // 兜底：有些项目把工程图放在零件的子文件夹里，再找一层。
                foreach (var sub in Directory.GetDirectories(directory))
                {
                    try
                    {
                        foreach (var candidate in Directory.GetFiles(sub, stem + ".slddrw"))
                        {
                            return candidate;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("在子文件夹查找工程图失败：" + ex.Message);
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                Log.Warn("查找工程图失败：" + ex.Message);
                return string.Empty;
            }
        }

        private static void ActivateDocument(ISldWorks swApp, ModelDoc2 doc)
        {
            try
            {
                var title = doc.GetTitle();
                if (string.IsNullOrEmpty(title))
                {
                    return;
                }

                int errors = 0;
                swApp.ActivateDoc3(title, false,
                    (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref errors);
            }
            catch (Exception ex)
            {
                Log.Warn("切换到已打开的文档失败：" + ex.Message);
            }
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

        /// <summary>
        /// 按 BOM 编辑值和当前命名规则反推零件文件名，再通过 RenameDocument
        /// 重命名文件并同步装配引用。不会写入名称、材料、工艺或备注属性。
        /// </summary>
        public static int RenameAssemblyFilesByBomRules(ISldWorks swApp, IList<PartListRow> rows,
            NamingOptions naming, Action<string> log)
        {
            var renamed = 0;
            log = log ?? delegate { };
            if (swApp == null || rows == null || naming == null)
            {
                return renamed;
            }

            var doc = SwUtils.ActiveDoc(swApp);
            var assembly = doc as AssemblyDoc;
            if (doc == null || assembly == null)
            {
                log("✗ 当前文档不是装配体，无法重命名零件文件。");
                return renamed;
            }

            // 文件重命名对同一路径的所有实例同时生效，因此只按文件路径合并。
            var desiredByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.FilePath))
                {
                    continue;
                }

                // 直接改了「三维名称」的行按这个名字改名；其余按命名规则反推。
                var desired = row.HasFullNameEdit
                    ? NamingOptions.GetFileNameWithoutExtension(row.FullName).Trim().Trim('_', '-')
                    : row.HasNamingEdits
                        ? BuildComponentBaseName(row, naming)
                        : string.Empty;
                if (string.IsNullOrWhiteSpace(desired))
                {
                    continue;
                }

                string existing;
                if (desiredByPath.TryGetValue(row.FilePath, out existing))
                {
                    if (!string.Equals(existing, desired, StringComparison.OrdinalIgnoreCase))
                    {
                        log(string.Format("⚠ 同一零件文件存在多个目标名称，保留“{0}”，忽略“{1}”：{2}",
                            existing, desired, row.FileName));
                    }
                    continue;
                }

                desiredByPath[row.FilePath] = desired;
            }

            var components = assembly.GetComponents(false) as object[];
            if (components == null)
            {
                return renamed;
            }

            foreach (var target in desiredByPath)
            {
                Component2 component = null;
                foreach (var item in components)
                {
                    var candidate = item as Component2;
                    if (candidate == null)
                    {
                        continue;
                    }
                    try
                    {
                        if (string.Equals(candidate.GetPathName(), target.Key,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            component = candidate;
                            break;
                        }
                    }
                    catch
                    {
                        // 继续查找同一路径的其他实例。
                    }
                }

                if (component == null)
                {
                    log("✗ 当前装配体中找不到零件引用：" + target.Key);
                    continue;
                }

                try
                {
                    var sourcePath = target.Key;
                    var desired = target.Value;
                    var currentBase = NamingOptions.GetFileNameWithoutExtension(sourcePath);
                    if (string.Equals(currentBase, desired, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var row in rows)
                        {
                            if (row != null && string.Equals(row.FilePath, sourcePath,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                row.MarkNamingClean();
                            }
                        }
                        continue;
                    }

                    var targetPath = Path.Combine(Path.GetDirectoryName(sourcePath) ?? string.Empty,
                        desired + Path.GetExtension(sourcePath));
                    if (File.Exists(targetPath))
                    {
                        log("✗ 目标文件已存在：" + targetPath);
                        continue;
                    }

                    var suppression = component.GetSuppression();
                    if (suppression == (int)swComponentSuppressionState_e.swComponentLightweight ||
                        suppression == (int)swComponentSuppressionState_e.swComponentFullyLightweight)
                    {
                        component.SetSuppression2((int)swComponentSuppressionState_e.swComponentResolved);
                    }

                    doc.ClearSelection2(true);
                    if (!component.Select4(false, null, false))
                    {
                        log("✗ 无法选择组件：" + currentBase);
                        continue;
                    }

                    var error = doc.Extension.RenameDocument(desired);
                    if (error != (int)swRenameDocumentError_e.swRenameDocumentError_None)
                    {
                        log(string.Format("✗ 文件重命名失败：{0} → {1}（错误 {2}：{3}）",
                            currentBase, desired, error, DescribeRenameError(error)));
                        continue;
                    }

                    renamed++;
                    log(string.Format("✓ 零件文件重命名：{0} → {1}",
                        Path.GetFileName(sourcePath), Path.GetFileName(targetPath)));

                    foreach (var row in rows)
                    {
                        if (row != null && string.Equals(row.FilePath, sourcePath,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            row.FilePath = targetPath;
                            row.PartNumber = desired;
                            row.FullName = desired;
                            row.MarkNamingClean();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("重命名零件文件失败：" + ex.Message);
                    log("✗ 零件文件重命名失败：" + ex.Message);
                }
            }

            if (renamed > 0)
            {
                try
                {
                    doc.EditRebuild3();
                    int saveErrors = 0;
                    int saveWarnings = 0;
                    var saved = doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                        ref saveErrors, ref saveWarnings);
                    if (!saved || saveErrors != 0)
                    {
                        log(string.Format("⚠ 文件已重命名，但装配体保存失败（错误 {0}）。", saveErrors));
                    }
                    else
                    {
                        log("✓ 当前装配体已保存，装配引用已同步到新文件名。");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("保存重命名后的装配体失败：" + ex.Message);
                    log("⚠ 文件已重命名，但装配体保存失败：" + ex.Message);
                }
            }

            return renamed;
        }

        internal static string BuildComponentBaseName(PartListRow row, NamingOptions naming)
        {
            if (row == null || naming == null)
            {
                return string.Empty;
            }

            if (string.Equals(row.Classification, "标准件", StringComparison.Ordinal))
            {
                var fields = new List<string>();
                AddNameField(fields, row.Process);
                AddNameField(fields, row.Name);
                AddNameField(fields, row.Material);
                return SanitizeComponentName(string.Join("-", fields.ToArray()));
            }

            if (string.Equals(row.Classification, "加工件", StringComparison.Ordinal))
            {
                var stem = NamingOptions.GetFileNameWithoutExtension(row.FilePath).Trim();
                var separator = DetectMachinedSeparator(stem);
                var parts = stem.Split(new[] { separator }, StringSplitOptions.None);
                var nameIndex = -1;
                var materialIndex = -1;
                for (var i = 0; i < naming.MachinedSegments.Length; i++)
                {
                    if (naming.MachinedSegments[i] == MachinedSegmentKind.Name)
                    {
                        nameIndex = i;
                    }
                    if (naming.MachinedSegments[i] == MachinedSegmentKind.Material)
                    {
                        materialIndex = i;
                    }
                }

                if (parts.Length < naming.MachinedSegments.Length)
                {
                    Array.Resize(ref parts, naming.MachinedSegments.Length);
                }

                if (materialIndex >= 0 && materialIndex < parts.Length)
                {
                    var materialToken = ResolveMachinedMaterialToken(
                        row, naming, parts[materialIndex]);
                    if (!string.IsNullOrWhiteSpace(materialToken))
                    {
                        parts[materialIndex] = materialToken;
                    }
                }

                if (nameIndex >= 0 && nameIndex < parts.Length && !string.IsNullOrWhiteSpace(row.Name))
                {
                    var selectedIndexes = new List<int>();
                    if (naming.MachinedSegmentBomNameFlags != null &&
                        naming.MachinedSegmentBomNameFlags.Length == naming.MachinedSegments.Length)
                    {
                        for (var i = 0; i < naming.MachinedSegmentBomNameFlags.Length && i < parts.Length; i++)
                        {
                            if (naming.MachinedSegmentBomNameFlags[i])
                            {
                                selectedIndexes.Add(i);
                            }
                        }
                    }

                    var editedValues = row.Name.Trim().Split(new[] { '_', '-' }, StringSplitOptions.None);
                    if (selectedIndexes.Count > 1 && editedValues.Length == selectedIndexes.Count)
                    {
                        for (var i = 0; i < selectedIndexes.Count; i++)
                        {
                            parts[selectedIndexes[i]] = editedValues[i].Trim();
                        }
                    }
                    else
                    {
                        parts[nameIndex] = row.Name.Trim();
                    }
                }

                return SanitizeComponentName(string.Join("-", parts));
            }

            return SanitizeComponentName(row.Name);
        }

        private static string ResolveMachinedMaterialToken(PartListRow row, NamingOptions naming,
            string currentToken)
        {
            var material = (row.Material ?? string.Empty).Trim();
            var process = (row.Process ?? string.Empty).Trim();
            var presets = naming.MachinedMaterialProcessPresets;
            if (presets != null)
            {
                MaterialProcessPreset currentPreset;
                if (!string.IsNullOrWhiteSpace(currentToken) &&
                    presets.TryGetValue(currentToken.Trim(), out currentPreset) &&
                    MatchesMaterialProcess(currentPreset, material, process))
                {
                    return currentToken.Trim();
                }

                string matched = null;
                foreach (var preset in presets)
                {
                    if (!MatchesMaterialProcess(preset.Value, material, process))
                    {
                        continue;
                    }

                    if (string.Equals(preset.Key, material, StringComparison.OrdinalIgnoreCase))
                    {
                        return preset.Key;
                    }
                    if (matched == null || string.Compare(preset.Key, matched,
                            StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        matched = preset.Key;
                    }
                }

                if (matched != null)
                {
                    return matched;
                }
            }

            return SanitizeComponentName(material);
        }

        private static bool MatchesMaterialProcess(MaterialProcessPreset preset,
            string material, string process)
        {
            return preset != null &&
                   string.Equals((preset.Material ?? string.Empty).Trim(), material,
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals((preset.Process ?? string.Empty).Trim(), process,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string DescribeRenameError(int error)
        {
            switch ((swRenameDocumentError_e)error)
            {
                case swRenameDocumentError_e.swRenameDocumentError_ComponentNotResolved: return "组件未解析";
                case swRenameDocumentError_e.swRenameDocumentError_LightWeightComponent: return "轻化组件尚未完全解析";
                case swRenameDocumentError_e.swRenameDocumentError_NoModelLoaded: return "组件模型尚未载入";
                case swRenameDocumentError_e.swRenameDocumentError_FileAlreadyExists: return "目标文件已存在";
                case swRenameDocumentError_e.swRenameDocumentError_InvalidCharactersInName: return "名称含无效字符";
                case swRenameDocumentError_e.swRenameDocumentError_ReadOnlyDocument: return "文件或引用为只读";
                case swRenameDocumentError_e.swRenameDocumentError_DocumentNameInUse: return "目标名称正在使用";
                case swRenameDocumentError_e.swRenameDocumentError_ToolboxComponent: return "Toolbox 组件不能这样重命名";
                case swRenameDocumentError_e.swRenameDocumentError_PatternedComponent: return "阵列组件不能这样重命名";
                default: return "请确认组件已解析，且允许从 FeatureManager 树重命名组件文件";
            }
        }

        private static void AddNameField(ICollection<string> fields, string value)
        {
            var clean = (value ?? string.Empty).Trim().Trim('_', '-');
            if (clean.Length > 0)
            {
                fields.Add(clean);
            }
        }

        private static char DetectMachinedSeparator(string stem)
        {
            var digits = 0;
            while (digits < stem.Length && char.IsDigit(stem[digits]))
            {
                digits++;
            }
            if (digits >= 6 && digits < stem.Length && (stem[digits] == '_' || stem[digits] == '-'))
            {
                return stem[digits];
            }
            return '-';
        }

        private static string ComponentReferenceKey(string path, string configuration)
        {
            return (path ?? string.Empty).Trim().ToLowerInvariant() + "|" +
                   (configuration ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string PreserveComponentInstancePathAndSuffix(string current, string filePath,
            string desiredBase)
        {
            var slash = current.LastIndexOf('/');
            var parent = slash >= 0 ? current.Substring(0, slash + 1) : string.Empty;
            var leaf = slash >= 0 ? current.Substring(slash + 1) : current;
            var fileStem = NamingOptions.GetFileNameWithoutExtension(filePath);
            var suffix = string.Empty;

            if (!string.IsNullOrEmpty(fileStem) && leaf.StartsWith(fileStem, StringComparison.OrdinalIgnoreCase))
            {
                var candidate = leaf.Substring(fileStem.Length);
                if (IsInstanceSuffix(candidate))
                {
                    suffix = candidate;
                }
            }

            if (suffix.Length == 0)
            {
                var open = leaf.LastIndexOf('<');
                if (open > 0 && leaf.EndsWith(">", StringComparison.Ordinal) &&
                    IsDigits(leaf.Substring(open + 1, leaf.Length - open - 2)))
                {
                    suffix = leaf.Substring(open);
                }
            }

            return parent + desiredBase + suffix;
        }

        private static bool IsInstanceSuffix(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            if (value[0] == '-' && IsDigits(value.Substring(1)))
            {
                return true;
            }
            return value[0] == '<' && value.EndsWith(">", StringComparison.Ordinal) &&
                   IsDigits(value.Substring(1, value.Length - 2));
        }

        private static bool IsDigits(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            foreach (var character in value)
            {
                if (!char.IsDigit(character))
                {
                    return false;
                }
            }
            return true;
        }

        private static string SanitizeComponentName(string value)
        {
            var clean = (value ?? string.Empty).Trim();
            foreach (var invalid in new[] { '/', '\\', '@', '<', '>' })
            {
                clean = clean.Replace(invalid, '-');
            }
            return clean.Trim(' ', '_', '-');
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

            /// <summary>因不符合「日期-材料-名称」或「前缀-中间名-型号」被排除的组件数。</summary>
            public int SkippedByPattern { get; set; }

            /// <summary>因不符合「日期-装配」命名被整层忽略的子装配体数。</summary>
            public int SkippedAssemblies { get; set; }
        }

        /// <summary>
        /// 子装配体的读取策略：
        /// ① 以标准件前缀开头（淘宝 / 代理 / 整机…）= 整机外购件，整体作为标准件计入；
        /// ② 符合加工件命名规则（日期开头，如 20260425-装配-水箱总装）= 继续往下读取子零件；
        /// ③ 其它未按规则命名的装配体 = 整层忽略，不统计它内部的零件。
        /// </summary>
        internal static SubassemblyAction ResolveSubassemblyAction(NamingOptions naming, string name)
        {
            var ruleName = NamingOptions.GetFileNameWithoutExtension(name ?? string.Empty).Trim();
            if (ruleName.Length == 0 || naming == null)
            {
                return SubassemblyAction.Ignore;
            }

            // 关闭「按命名规则过滤」时保持旧行为：所有子装配体一律往下读取。
            if (!naming.RequireBomPattern)
            {
                return SubassemblyAction.Expand;
            }

            if (naming.HasKnownPrefix(NamingOptions.FirstSegment(ruleName)))
            {
                return SubassemblyAction.TreatAsStandard;
            }

            return naming.IsMachinedName(ruleName)
                ? SubassemblyAction.Expand
                : SubassemblyAction.Ignore;
        }

        /// <summary>
        /// 单个组件的归类：参考件不进 BOM；不符合「加工件 / 标准件」命名的记为未匹配，
        /// 由调用方排到表格最后并标红。
        /// </summary>
        internal static RowDisposition ResolveRowDisposition(NamingOptions naming, string name)
        {
            if (naming == null)
            {
                return RowDisposition.Matched;
            }

            var ruleName = NamingOptions.GetFileNameWithoutExtension(name ?? string.Empty).Trim();
            if (NamingOptions.IsReferenceName(ruleName))
            {
                return RowDisposition.Reference;
            }

            return naming.MatchesBomPattern(ruleName)
                ? RowDisposition.Matched
                : RowDisposition.Unmatched;
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
                    var assemblyName = ComponentRuleName(component, path);
                    if (string.IsNullOrEmpty(assemblyName))
                    {
                        assemblyName = ComponentDisplayName(component, path);
                    }

                    var action = ResolveSubassemblyAction(context.Options.Naming, assemblyName);

                    // 以标准件前缀开头（淘宝 / 代理 / 整机…）的装配体属于整机外购件：
                    // 作为一条标准件计入，不再往下读取它的子零件。
                    if (action == SubassemblyAction.TreatAsStandard)
                    {
                        Accumulate(context, component, path, hierarchy);
                        return;
                    }

                    // 只有按加工件命名规则（日期开头，例如 20260425-装配-水箱总装）命名的
                    // 子装配体才继续往下读取；其它装配体整层忽略，避免把未按规则命名的
                    // 装配体内部零件混进 BOM。
                    if (action == SubassemblyAction.Ignore)
                    {
                        context.SkippedAssemblies++;
                        // 不展开它内部的零件，但把这个子装配体记成一行“未匹配”，
                        // 排到表格最后并标红，用户一眼能看到哪个装配体没按规则命名。
                        Accumulate(context, component, path, hierarchy, true);
                        return;
                    }

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
            IList<string> hierarchy, bool unmatched = false)
        {
            var configuration = component.ReferencedConfiguration ?? string.Empty;
            var isVirtual = string.IsNullOrEmpty(path);
            var location = FormatLocation(hierarchy, context.Options.AssemblyLevel);

            // BOM 优先采用装配体中的组件实例名。这样“写入并重命名”之后立即刷新，
            // 表格仍会显示新名称；零件文件路径只负责定位和写入属性。
            var ruleName = ComponentRuleName(component, path);
            if (string.IsNullOrEmpty(ruleName))
            {
                ruleName = Path.GetFileName(path);
            }

            if (!unmatched)
            {
                // 参考件是明确规则：不进 BOM，也不需要标红提示。
                // 不符合加工件 / 标准件命名的组件不丢弃：改记成一行“未匹配”，
                // 排在表格最后并标红，提醒补齐命名规则。
                switch (ResolveRowDisposition(context.Options.Naming, ruleName))
                {
                    case RowDisposition.Reference:
                        return;

                    case RowDisposition.Unmatched:
                        context.SkippedByPattern++;
                        Accumulate(context, component, path, hierarchy, true);
                        return;
                }
            }

            var key = (unmatched ? "#unmatched|" : string.Empty) + (isVirtual
                ? "#virtual|" + (component.Name2 ?? string.Empty) + "|" + location
                : path.ToLowerInvariant() + "|" + configuration.ToLowerInvariant() + "|" + location);

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

            var displayPath = ruleName;

            if (NamingOptions.IsPlaceholder(modelMaterial))
            {
                modelMaterial = string.Empty;
            }

            var segmentMaterial = context.Options.Naming.ResolveMaterialFromSegments(displayPath);
            var propertyMaterial = First(properties, "材料", "材质", "Material", "材质牌号");
            var classification = unmatched
                ? UnmatchedClassification
                : Classify(context, path, ruleName, properties);

            row = new PartListRow
            {
                Location = location,
                FullName = ResolveFullName(displayPath),
                DrawingPath = FindDrawingFor(path),
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
                IsUnmatched = unmatched,
                FilePath = path,
                Configuration = configuration
            };

            ApplyMachinedFields(row, displayPath, context.Options.Naming);
            ApplyStandardFields(row, displayPath, context.Options.Naming);
            if (string.IsNullOrEmpty(row.Name))
            {
                row.Name = context.Options.Naming.ResolveName(displayPath, name => Lookup(properties, name));
            }
            ApplyConfiguredFields(row, displayPath, properties, context.Options);

            context.Rows[key] = row;
        }

        private static string ResolveFullName(string value)
        {
            return (NamingOptions.GetFileNameWithoutExtension(value) ?? string.Empty).Trim();
        }

        private static string ComponentRuleName(Component2 component, string path)
        {
            var value = component == null ? string.Empty : component.Name2 ?? string.Empty;
            var slash = value.LastIndexOf('/');
            if (slash >= 0 && slash < value.Length - 1)
            {
                value = value.Substring(slash + 1);
            }

            var fileStem = NamingOptions.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(fileStem) && value.StartsWith(fileStem, StringComparison.OrdinalIgnoreCase))
            {
                var suffix = value.Substring(fileStem.Length);
                if (IsInstanceSuffix(suffix))
                {
                    return fileStem;
                }
            }

            var open = value.LastIndexOf('<');
            if (open > 0 && value.EndsWith(">", StringComparison.Ordinal) &&
                IsDigits(value.Substring(open + 1, value.Length - open - 2)))
            {
                value = value.Substring(0, open);
            }
            else
            {
                var dash = value.LastIndexOf('-');
                if (dash > 0 && IsDigits(value.Substring(dash + 1)))
                {
                    value = value.Substring(0, dash);
                }
            }

            return value.Trim();
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
        /// 标准件统一按“前缀-中文中间名-原始名称或型号”解释，同时兼容旧下划线：
        /// 工艺=前缀，零件名=中文中间名，材料/型号=第 3 段及以后。
        /// 例如电气-接近开关-LJ12A3-ZBX 会得到：标准件 / 接近开关 / LJ12A3-ZBX / 电气。
        /// </summary>
        private static void ApplyStandardFields(PartListRow row, string sourceName, NamingOptions naming)
        {
            if (row == null || naming == null ||
                !string.Equals(row.Classification, "标准件", StringComparison.Ordinal))
            {
                return;
            }

            var stem = NamingOptions.GetFileNameWithoutExtension(sourceName).Trim();
            var separator = DetectStandardSeparator(stem);
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
                row.Material = string.Join(separator.ToString(), originalName).Trim();
            }
            else
            {
                row.Material = string.Empty;
            }
        }

        private static void ApplyMachinedFields(PartListRow row, string sourceName, NamingOptions naming)
        {
            if (row == null || naming == null ||
                !string.Equals(row.Classification, "加工件", StringComparison.Ordinal))
            {
                return;
            }

            string material;
            string process;
            string surfaceTreatment;
            if (naming.TryResolveMachinedMaterialProcessSurface(sourceName, out material, out process,
                    out surfaceTreatment))
            {
                row.Material = material;
                row.Process = process;
                row.SurfaceTreatment = surfaceTreatment;
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
            var assemblyNoteField = standard
                ? options.StandardAssemblyNoteField
                : ResolveRuleField(options.MachinedAssemblyNoteField, options.Naming);
            row.AssemblyNote = ResolveConfiguredFieldWithNaming(
                assemblyNoteField,
                string.Empty, sourceName, properties, standard, options.Naming);
            var nameField = standard
                ? options.StandardNameField
                : ResolveRuleField(options.MachinedNameField, options.Naming);
            row.Name = ResolveConfiguredFieldWithNaming(
                nameField,
                row.Name, sourceName, properties, standard, options.Naming);
            row.Material = ResolveConfiguredFieldWithNaming(
                standard ? options.StandardMaterialField : options.MachinedMaterialField,
                row.Material, sourceName, properties, standard, options.Naming);
            row.Process = ResolveConfiguredFieldWithNaming(
                standard ? options.StandardProcessField : options.MachinedProcessField,
                row.Process, sourceName, properties, standard, options.Naming);
            var surfaceField = standard
                ? options.StandardSurfaceField
                : ResolveRuleField(options.MachinedSurfaceField, options.Naming);
            row.SurfaceTreatment = ResolveConfiguredFieldWithNaming(
                surfaceField, row.SurfaceTreatment, sourceName, properties, standard, options.Naming);
            row.Remark = ResolveConfiguredFieldWithNaming(
                standard ? options.StandardRemarkField : options.MachinedRemarkField,
                row.Remark, sourceName, properties, standard, options.Naming);
        }

        private static string ResolveConfiguredField(string setting, string automaticValue,
            string sourceName, Dictionary<string, string> properties, bool standard)
        {
            return ResolveConfiguredFieldWithNaming(setting, automaticValue, sourceName,
                properties, standard, null);
        }

        private static string ResolveConfiguredFieldWithNaming(string setting, string automaticValue,
            string sourceName, Dictionary<string, string> properties, bool standard,
            NamingOptions naming)
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

            if (!standard && source.StartsWith("machined2:", StringComparison.Ordinal) &&
                naming != null)
            {
                string material;
                string process;
                string surface;
                if (!naming.TryResolveMachinedMaterialProcessSurface(sourceName,
                        out material, out process, out surface))
                {
                    return string.Empty;
                }

                switch (source)
                {
                    case "machined2:material": return material ?? string.Empty;
                    case "machined2:process": return process ?? string.Empty;
                    case "machined2:surface": return surface ?? string.Empty;
                }
            }

            if (source.StartsWith("segment:", StringComparison.Ordinal))
            {
                int number;
                if (int.TryParse(source.Substring("segment:".Length), out number) && number > 0)
                {
                    var parts = SplitConfiguredSegments(stem, standard);
                    return number <= parts.Length ? parts[number - 1].Trim() : string.Empty;
                }

                return string.Empty;
            }

            if (source.StartsWith("tail:", StringComparison.Ordinal))
            {
                int number;
                if (int.TryParse(source.Substring("tail:".Length), out number) && number > 0)
                {
                    var parts = SplitConfiguredSegments(stem, standard);
                    if (number <= parts.Length)
                    {
                        var tail = new string[parts.Length - number + 1];
                        Array.Copy(parts, number - 1, tail, 0, tail.Length);
                        var separator = standard ? DetectStandardSeparator(stem) : DetectMachinedSeparator(stem);
                        return string.Join(separator.ToString(), tail).Trim();
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
                case "property:assemblynote":
                    return First(properties, "安装说明", "装配说明", "装配备注", "AssemblyNote", "Assembly Note");
                case "property:surface":
                    return First(properties, "表面处理", "表面", "表面工艺", "SurfaceTreatment", "Surface Treatment", "Finish");
                default:
                    return automaticValue ?? string.Empty;
            }
        }

        private static string ResolveRuleField(string setting, NamingOptions naming)
        {
            var rawValue = (setting ?? string.Empty).Trim();
            var value = rawValue.ToLowerInvariant();
            if (!value.StartsWith("rule:", StringComparison.Ordinal) || naming == null ||
                naming.MachinedSegments == null)
            {
                return setting;
            }

            var labels = naming.MachinedSegmentLabels ?? new string[0];
            for (var index = 0; index < naming.MachinedSegments.Length; index++)
            {
                var label = index < labels.Length ? (labels[index] ?? string.Empty).Trim() : string.Empty;
                var kind = naming.MachinedSegments[index];
                if ((value == "rule:date" && kind == MachinedSegmentKind.Date) ||
                    (value == "rule:material" && kind == MachinedSegmentKind.Material) ||
                    (value == "rule:name" && kind == MachinedSegmentKind.Name) ||
                    (value == "rule:version" && kind == MachinedSegmentKind.Serial) ||
                    (value == "rule:extension" && kind == MachinedSegmentKind.Extension))
                {
                    return "segment:" + (index + 1);
                }
                if (value == "rule:surface" &&
                    (label == "表面处理" || label == "表面工艺" || label == "表面"))
                {
                    return "segment:" + (index + 1);
                }
                if (value == "rule:assemblynote" &&
                    (label == "安装说明" || label == "装配说明" || label == "装配备注"))
                {
                    return "segment:" + (index + 1);
                }
                if (value.StartsWith("rule:label:", StringComparison.Ordinal) &&
                    string.Equals(label, rawValue.Substring("rule:label:".Length),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return "segment:" + (index + 1);
                }
            }

            return value == "rule:surface" ? "auto" : "empty";
        }

        /// <summary>
        /// 标准件和加工件均兼容下划线或短横线；新名称统一输出短横线。
        /// </summary>
        private static string[] SplitConfiguredSegments(string stem, bool standard)
        {
            if (string.IsNullOrEmpty(stem))
            {
                return new string[0];
            }

            if (standard)
            {
                return stem.Split(new[] { DetectStandardSeparator(stem) }, StringSplitOptions.None);
            }

            var separator = '-';
            var digits = 0;
            while (digits < stem.Length && char.IsDigit(stem[digits]))
            {
                digits++;
            }

            if (digits >= 6 && digits < stem.Length &&
                (stem[digits] == '_' || stem[digits] == '-'))
            {
                separator = stem[digits];
            }

            return stem.Split(new[] { separator }, StringSplitOptions.None);
        }

        private static char DetectStandardSeparator(string stem)
        {
            if (string.IsNullOrEmpty(stem))
            {
                return '-';
            }
            var underscore = stem.IndexOf('_');
            var hyphen = stem.IndexOf('-');
            if (underscore < 0) return '-';
            if (hyphen < 0) return '_';
            return underscore < hyphen ? '_' : '-';
        }

        private static string Classify(CollectContext context, string path, string name, Dictionary<string, string> properties)
        {
            // 命名规则优先：参考- = 参考件；日期开头 = 加工件；已知前缀开头 = 标准件
            var ruleName = NamingOptions.GetFileNameWithoutExtension(
                string.IsNullOrEmpty(name) ? path : name).Trim();

            if (NamingOptions.IsReferenceName(ruleName))
            {
                return "参考件";
            }

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
                // 不符合命名规则的组件统一排到最后，方便一眼看到需要补规则的部分。
                if (left.IsUnmatched != right.IsUnmatched)
                {
                    return left.IsUnmatched ? 1 : -1;
                }

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
