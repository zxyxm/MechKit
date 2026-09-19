using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace MechKit.Core
{
    /// <summary>
    /// 轻量配置存储（key=value 文本），避免引用第三方 JSON 库。
    /// </summary>
    public sealed class AddinSettings
    {
        public const string PortableFileName = "MechKit-settings.ini";

        private readonly Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 放在 MechKit.dll 同目录时会自动生效的便携配置文件。
        /// 安装版中通常为 C:\MechKit\MechKit-settings.ini。
        /// </summary>
        public static string PortableSettingsPath
        {
            get
            {
                var location = Assembly.GetExecutingAssembly().Location;
                var directory = string.IsNullOrEmpty(location)
                    ? AppDomain.CurrentDomain.BaseDirectory
                    : Path.GetDirectoryName(location);
                return Path.Combine(directory ?? AppDomain.CurrentDomain.BaseDirectory, PortableFileName);
            }
        }

        public string SourceFolder
        {
            get { return Get("SourceFolder", string.Empty); }
            set { Set("SourceFolder", value); }
        }

        public string OutputFolder
        {
            get { return Get("OutputFolder", string.Empty); }
            set { Set("OutputFolder", value); }
        }

        public string PropertyFolder
        {
            get { return Get("PropertyFolder", string.Empty); }
            set { Set("PropertyFolder", value); }
        }

        public bool ExportRecursive
        {
            get { return GetBool("ExportRecursive", true); }
            set { SetBool("ExportRecursive", value); }
        }

        public bool ExportKeepTree
        {
            get { return GetBool("ExportKeepTree", true); }
            set { SetBool("ExportKeepTree", value); }
        }

        public string ExportFormats
        {
            get { return Get("ExportFormats", ".pdf"); }
            set { Set("ExportFormats", value); }
        }

        /// <summary>图号来源（PartNumberSource）。</summary>
        public int PartNumberSource
        {
            get { return GetInt("PartNumberSource", 0); }
            set { SetInt("PartNumberSource", value); }
        }

        /// <summary>文件名截断规则（FileNameCutRule）。</summary>
        public int PartNumberCutRule
        {
            get { return GetInt("PartNumberCutRule", 1); }
            set { SetInt("PartNumberCutRule", value); }
        }

        public string PartNumberPattern
        {
            get { return Get("PartNumberPattern", string.Empty); }
            set { Set("PartNumberPattern", value); }
        }

        /// <summary>材料来源（MaterialSource）。</summary>
        public int MaterialSource
        {
            get { return GetInt("MaterialSource", 0); }
            set { SetInt("MaterialSource", value); }
        }

        public string PartNumberProperty
        {
            get { return Get("PartNumberProperty", "图号"); }
            set { Set("PartNumberProperty", value); }
        }

        public string MaterialProperty
        {
            get { return Get("MaterialProperty", "材料"); }
            set { Set("MaterialProperty", value); }
        }

        /// <summary>是否按分隔符把文件名分段解析名称/材料（日期-材料-名称，兼容旧下划线）。</summary>
        public bool UseNameSegments
        {
            get { return GetBool("UseNameSegments", true); }
            set { SetBool("UseNameSegments", value); }
        }

        public string SegmentSeparator
        {
            get { return Get("SegmentSeparator", "-_"); }
            set { Set("SegmentSeparator", value); }
        }

        /// <summary>名称所在段，-1 = 最后一段。</summary>
        public int NameSegment
        {
            get { return GetInt("NameSegment", -1); }
            set { SetInt("NameSegment", value); }
        }

        /// <summary>材料所在段，-2 = 倒数第二段，0 = 不取。</summary>
        public int MaterialSegment
        {
            get { return GetInt("MaterialSegment", -2); }
            set { SetInt("MaterialSegment", value); }
        }

        /// <summary>加工件段顺序，例如 date,material,name,serial。</summary>
        public string MachinedSegments
        {
            get { return Get("MachinedSegments", "date,material,name,serial,custom"); }
            set { Set("MachinedSegments", value); }
        }

        /// <summary>与加工件段一一对应的自定义显示名称，使用竖线分隔并转义。</summary>
        public string MachinedSegmentLabels
        {
            get { return Get("MachinedSegmentLabels", "||||安装说明"); }
            set { Set("MachinedSegmentLabels", value); }
        }

        /// <summary>加工件各段是否并入 BOM 零件名，使用 | 分隔。</summary>
        public string MachinedSegmentBomNameFlags
        {
            get { return Get("MachinedSegmentBomNameFlags", "0|0|1|1|1"); }
            set { Set("MachinedSegmentBomNameFlags", value); }
        }

        /// <summary>加工件材料段预设；每行 key=材料|工艺|表面处理，兼容旧“材料-工艺”。</summary>
        public string MachinedMaterialProcessRules
        {
            get
            {
                return Get("MachinedMaterialProcessRules",
                    "6061=6061|cnc|本色氧化\n5052=5052|钣金|白色细砂纹烤漆\n304=304|cnc|\n轴304=304|车铣|\n淘宝=-|追加工|");
            }
            set { Set("MachinedMaterialProcessRules", value); }
        }

        /// <summary>加工件选项卡第二级快捷字段（通常为材料牌号）。</summary>
        public string MachinedLevel2Values
        {
            get { return Get("MachinedLevel2Values", "6061 5052 304 轴304"); }
            set { Set("MachinedLevel2Values", value); }
        }

        /// <summary>加工件二级字段中显示在 CommandManager 选项卡上的值。</summary>
        public string MachinedTabLevel2Values
        {
            get { return Get("MachinedTabLevel2Values", "6061 5052 304 轴304"); }
            set { Set("MachinedTabLevel2Values", value); }
        }

        /// <summary>加工件选项卡第三级快捷字段（通常为常用零件名称）。</summary>
        public string MachinedLevel3Values
        {
            get { return Get("MachinedLevel3Values", string.Empty); }
            set { Set("MachinedLevel3Values", value); }
        }

        /// <summary>加工件三级字段中显示在 CommandManager 选项卡上的值。</summary>
        public string MachinedTabLevel3Values
        {
            get { return Get("MachinedTabLevel3Values", string.Empty); }
            set { Set("MachinedTabLevel3Values", value); }
        }

        public bool PartListOnlyMachined
        {
            get { return GetBool("PartListOnlyMachined", false); }
            set { SetBool("PartListOnlyMachined", value); }
        }

        /// <summary>按厂商关键词识别外购件/标准件。</summary>
        public bool DetectVendorParts
        {
            get { return GetBool("DetectVendorParts", true); }
            set { SetBool("DetectVendorParts", value); }
        }

        public bool PartListReadProperties
        {
            get { return GetBool("PartListReadProperties", true); }
            set { SetBool("PartListReadProperties", value); }
        }

        /// <summary>常用目录（个人习惯），留空时按 SOLIDWORKS 默认位置自动探测。</summary>
        public string WeldmentFolder
        {
            get { return Get("WeldmentFolder", string.Empty); }
            set { Set("WeldmentFolder", value); }
        }

        /// <summary>随包焊件库（GB焊接轮廓）所在目录，用于一键迁移。</summary>
        public string WeldmentLibrarySource
        {
            get { return Get("WeldmentLibrarySource", string.Empty); }
            set { Set("WeldmentLibrarySource", value); }
        }

        public string TemplateFolder
        {
            get { return Get("TemplateFolder", string.Empty); }
            set { Set("TemplateFolder", value); }
        }

        public string MacroFolder
        {
            get { return Get("MacroFolder", string.Empty); }
            set { Set("MacroFolder", value); }
        }

        public string ToolboxFolder
        {
            get { return Get("ToolboxFolder", string.Empty); }
            set { Set("ToolboxFolder", value); }
        }

        public string SettingsBackupFolder
        {
            get { return Get("SettingsBackupFolder", string.Empty); }
            set { Set("SettingsBackupFolder", value); }
        }

        /// <summary>BOM 收录用的名称前缀，空格分隔，例如：电机 电气 淘宝。</summary>
        public string BomPrefixes
        {
            get { return Get("BomPrefixes", "淘宝 代理 淘宝追加工"); }
            set { Set("BomPrefixes", value); }
        }

        /// <summary>标准件前缀说明，使用 URI 转义的 key=value|key=value 格式保存。</summary>
        public string BomPrefixDescriptions
        {
            get { return Get("BomPrefixDescriptions", string.Empty); }
            set { Set("BomPrefixDescriptions", value); }
        }

        /// <summary>标准件中文中间名，空格分隔，例如：接近开关 磁吸开关。</summary>
        public string BomMiddleNames
        {
            get { return Get("BomMiddleNames", "接近开关 直线导轨 电机 丝杆"); }
            set { Set("BomMiddleNames", value); }
        }

        /// <summary>标准件二级字段备注，使用 URI 转义的 key=value|key=value 格式保存。</summary>
        public string BomMiddleNameDescriptions
        {
            get { return Get("BomMiddleNameDescriptions", string.Empty); }
            set { Set("BomMiddleNameDescriptions", value); }
        }

        /// <summary>标准件一级字段中显示在 CommandManager 选项卡上的项目。</summary>
        public string StandardTabPrefixes
        {
            get { return Get("StandardTabPrefixes", "淘宝 代理 淘宝追加工"); }
            set { Set("StandardTabPrefixes", value); }
        }

        /// <summary>标准件二级字段中显示在 CommandManager 选项卡上的项目。</summary>
        public string StandardTabMiddleNames
        {
            get { return Get("StandardTabMiddleNames", "接近开关 直线导轨 电机 丝杆"); }
            set { Set("StandardTabMiddleNames", value); }
        }

        /// <summary>是否强制标准件“中间名 → 前缀”绑定。</summary>
        public bool StandardPrefixBindingEnabled
        {
            get { return GetBool("StandardPrefixBindingEnabled", true); }
            set { SetBool("StandardPrefixBindingEnabled", value); }
        }

        /// <summary>标准件绑定规则，格式：中间名=前缀|中间名=前缀。</summary>
        public string StandardPrefixBindings
        {
            get { return Get("StandardPrefixBindings", "电机=代理|接近开关=代理|直线导轨=代理"); }
            set { Set("StandardPrefixBindings", value); }
        }

        public int NamingPresetVersion
        {
            get { return GetInt("NamingPresetVersion", 0); }
            set { SetInt("NamingPresetVersion", value); }
        }

        /// <summary>是否只收录符合加工件日期格式或标准件前缀格式的零件。</summary>
        public bool BomRequirePattern
        {
            get { return GetBool("BomRequirePattern", true); }
            set { SetBool("BomRequirePattern", value); }
        }

        /// <summary>
        /// BOM“位置”列显示的三级组织结构。0 = 总装，1 = 总装/部装，
        /// -1 = 总装/部装/零件或小装配体的完整路径。
        /// </summary>
        public int BomAssemblyLevel
        {
            get { return GetInt("BomAssemblyLevel", -1); }
            set { SetInt("BomAssemblyLevel", value); }
        }

        // BOM 列的取值来源：auto / whole / rule:* / segment:1..8 / tail:3 /
        // property:name|material|process|remark|assemblynote / empty。
        public bool BomUsePropertyFields
        {
            get { return GetBool("BomUsePropertyFields", true); }
            set { SetBool("BomUsePropertyFields", value); }
        }

        public string BomColumnOrder
        {
            get { return Get("BomColumnOrder", "sequence,location,fullname,classification,name,material,process,surface,quantity,assemblynote,remark"); }
            set { Set("BomColumnOrder", value); }
        }

        public string BomAssemblyNoteHeader
        {
            get { return Get("BomAssemblyNoteHeader", "安装说明"); }
            set { Set("BomAssemblyNoteHeader", value); }
        }

        public string BomSequenceHeader
        {
            get { return Get("BomSequenceHeader", "序号"); }
            set { Set("BomSequenceHeader", value); }
        }

        public string BomLocationHeader
        {
            get { return Get("BomLocationHeader", "位置"); }
            set { Set("BomLocationHeader", value); }
        }

        public string BomFullNameHeader
        {
            get { return Get("BomFullNameHeader", "完整名称"); }
            set { Set("BomFullNameHeader", value); }
        }

        /// <summary>BOM「二维工程图」列的表头文字。</summary>
        public string BomDrawingHeader
        {
            get { return Get("BomDrawingHeader", "二维工程图"); }
            set { Set("BomDrawingHeader", value); }
        }

        public string BomClassificationHeader
        {
            get { return Get("BomClassificationHeader", "属性"); }
            set { Set("BomClassificationHeader", value); }
        }

        public string BomNameHeader
        {
            get { return Get("BomNameHeader", "零件名称/标准件名称"); }
            set { Set("BomNameHeader", value); }
        }

        public string BomMaterialHeader
        {
            get { return Get("BomMaterialHeader", "材料/型号"); }
            set { Set("BomMaterialHeader", value); }
        }

        public string BomProcessHeader
        {
            get { return Get("BomProcessHeader", "工艺/渠道"); }
            set { Set("BomProcessHeader", value); }
        }

        public string BomSurfaceHeader
        {
            get { return Get("BomSurfaceHeader", "表面处理"); }
            set { Set("BomSurfaceHeader", value); }
        }

        public string BomQuantityHeader
        {
            get { return Get("BomQuantityHeader", "数量"); }
            set { Set("BomQuantityHeader", value); }
        }

        public string BomRemarkHeader
        {
            get { return Get("BomRemarkHeader", "备注"); }
            set { Set("BomRemarkHeader", value); }
        }

        public string BomStandardAssemblyNoteField
        {
            get { return Get("BomStandardAssemblyNoteField", "auto"); }
            set { Set("BomStandardAssemblyNoteField", value); }
        }

        public string BomMachinedAssemblyNoteField
        {
            get { return Get("BomMachinedAssemblyNoteField", "rule:assemblynote"); }
            set { Set("BomMachinedAssemblyNoteField", value); }
        }

        public string BomStandardSurfaceField
        {
            get { return Get("BomStandardSurfaceField", "empty"); }
            set { Set("BomStandardSurfaceField", value); }
        }

        public string BomMachinedSurfaceField
        {
            get { return Get("BomMachinedSurfaceField", "rule:surface"); }
            set { Set("BomMachinedSurfaceField", value); }
        }

        public string BomStandardNameField
        {
            get { return Get("BomStandardNameField", "tail:3"); }
            set { Set("BomStandardNameField", value); }
        }

        public string BomStandardMaterialField
        {
            get { return Get("BomStandardMaterialField", "empty"); }
            set { Set("BomStandardMaterialField", value); }
        }

        public string BomStandardProcessField
        {
            get { return Get("BomStandardProcessField", "segment:2"); }
            set { Set("BomStandardProcessField", value); }
        }

        public string BomStandardRemarkField
        {
            get { return Get("BomStandardRemarkField", "auto"); }
            set { Set("BomStandardRemarkField", value); }
        }

        public string BomMachinedNameField
        {
            get { return Get("BomMachinedNameField", "auto"); }
            set { Set("BomMachinedNameField", value); }
        }

        public string BomMachinedMaterialField
        {
            get { return Get("BomMachinedMaterialField", "auto"); }
            set { Set("BomMachinedMaterialField", value); }
        }

        public string BomMachinedProcessField
        {
            get { return Get("BomMachinedProcessField", "auto"); }
            set { Set("BomMachinedProcessField", value); }
        }

        public string BomMachinedRemarkField
        {
            get { return Get("BomMachinedRemarkField", "property:remark"); }
            set { Set("BomMachinedRemarkField", value); }
        }

        public static AddinSettings Load()
        {
            var settings = new AddinSettings();

            try
            {
                LoadFile(settings, AppPaths.SettingsFile);

                // DLL 同目录的便携配置最后读取，从而覆盖本机配置。
                // 用户只需把导出的文件放到安装根目录并重启 SOLIDWORKS。
                var portableFile = PortableSettingsPath;
                if (!string.Equals(portableFile, AppPaths.SettingsFile,
                        StringComparison.OrdinalIgnoreCase) && File.Exists(portableFile))
                {
                    LoadFile(settings, portableFile);
                    Log.Info("已加载便携配置：" + portableFile);
                }

                // 首次升级到三段式标准件命名时，把旧默认映射迁移为：
                // 工艺=第1段、名称=第2段、材料/型号=第3段及以后。
                if (!settings._values.ContainsKey("BomMiddleNames"))
                {
                    settings.BomMiddleNames = "接近开关 磁吸开关";
                    if (settings.BomStandardNameField == "segment:3")
                    {
                        settings.BomStandardNameField = "segment:2";
                    }
                    if (settings.BomStandardMaterialField == "segment:2")
                    {
                        settings.BomStandardMaterialField = "tail:3";
                    }
                }

                if (settings.NamingPresetVersion < 1)
                {
                    settings.SegmentSeparator = "-_";
                    settings.MachinedSegments = "date,material,name,serial,custom";
                    settings.MachinedSegmentLabels = "||||拓展代号";
                    settings.MachinedMaterialProcessRules =
                        "6061=6061-cnc\n5052=5052-钣金\n304=304-cnc\n轴304=304-车铣\n淘宝=--追加工";
                    settings.BomPrefixes = "淘宝 代理 淘宝追加工";
                    settings.BomMiddleNames = "接近开关 直线导轨 电机 丝杆";
                    settings.StandardPrefixBindingEnabled = true;
                    settings.StandardPrefixBindings = "电机=代理|接近开关=代理|直线导轨=代理";
                    settings.BomMachinedMaterialField = "auto";
                    settings.BomMachinedProcessField = "auto";
                    settings.NamingPresetVersion = 2;
                }

                // v2：材料与工艺统一改用短横线分隔，不再在设置中保存中英文逗号。
                if (settings.NamingPresetVersion < 2)
                {
                    settings.MachinedMaterialProcessRules =
                        NamingOptionsFactory.NormalizeMaterialProcessPresetFormat(
                            settings.MachinedMaterialProcessRules);
                    settings.NamingPresetVersion = 2;
                }

                // v3：第 4 / 5 段规范为版本号和装配说明，并允许选择是否并入 BOM 零件名。
                if (settings.NamingPresetVersion < 3)
                {
                    var oldLayout = (settings.MachinedSegments ?? string.Empty).Trim();
                    if (string.Equals(oldLayout, "date,material,name,serial,custom",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        settings.MachinedSegments = "date,material,name,version,assemblynote";
                        settings.MachinedSegmentLabels = "||||";
                    }
                    else
                    {
                        settings.MachinedSegments = oldLayout.Replace("serial", "version");
                    }
                    var migratedSegments = NamingOptionsFactory.ParseMachinedSegments(settings.MachinedSegments);
                    settings.MachinedSegmentBomNameFlags =
                        NamingOptionsFactory.SerializeMachinedSegmentBomNameFlags(
                            NamingOptionsFactory.ParseMachinedSegmentBomNameFlags(
                                string.Empty, migratedSegments));
                    settings.NamingPresetVersion = 3;
                }

                // v4：与 BOM 字段设置统一，第 4 / 5 段回归“变更序号 / 拓展代号”。
                // 保留 v3 的勾选状态，只迁移字段语义和持久化键名。
                if (settings.NamingPresetVersion < 4)
                {
                    var migratedSegments = NamingOptionsFactory.ParseMachinedSegments(
                        settings.MachinedSegments);
                    var migratedFlags = NamingOptionsFactory.ParseMachinedSegmentBomNameFlags(
                        settings.MachinedSegmentBomNameFlags, migratedSegments);
                    settings.MachinedSegments =
                        NamingOptionsFactory.SerializeMachinedSegments(migratedSegments);
                    settings.MachinedSegmentBomNameFlags =
                        NamingOptionsFactory.SerializeMachinedSegmentBomNameFlags(migratedFlags);
                    settings.NamingPresetVersion = 4;
                }

                // v5：标准件快捷区增加“直线导轨”，并默认归入代理采购件。
                if (settings.NamingPresetVersion < 5)
                {
                    var middleNames = new List<string>(
                        NamingOptionsFactory.ParsePrefixes(settings.BomMiddleNames));
                    if (!middleNames.Exists(delegate(string item)
                        { return string.Equals(item, "直线导轨", StringComparison.OrdinalIgnoreCase); }))
                    {
                        var proximityIndex = middleNames.FindIndex(delegate(string item)
                        {
                            return string.Equals(item, "接近开关", StringComparison.OrdinalIgnoreCase);
                        });
                        middleNames.Insert(proximityIndex < 0 ? middleNames.Count : proximityIndex + 1,
                            "直线导轨");
                        settings.BomMiddleNames = NamingOptionsFactory.SerializePrefixes(middleNames);
                    }

                    var bindings = NamingOptionsFactory.ParsePrefixBindings(
                        settings.StandardPrefixBindings);
                    if (!bindings.ContainsKey("直线导轨"))
                    {
                        var currentBindings = (settings.StandardPrefixBindings ?? string.Empty).Trim().TrimEnd('|');
                        settings.StandardPrefixBindings =
                            (currentBindings.Length == 0 ? string.Empty : currentBindings + "|") +
                            "直线导轨=代理";
                    }

                    settings.NamingPresetVersion = 5;
                }

                // v6：BOM 固定为十列，并让安装说明/表面处理按命名规则中的同名段动态定位。
                if (settings.NamingPresetVersion < 6)
                {
                    if (string.Equals(settings.BomAssemblyNoteHeader, "装配说明",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        settings.BomAssemblyNoteHeader = "安装说明";
                    }
                    if (string.Equals(settings.BomMachinedAssemblyNoteField, "segment:5",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        settings.BomMachinedAssemblyNoteField = "rule:assemblynote";
                    }
                    settings.NamingPresetVersion = 6;
                }

                // v7：材料工艺预设增加表面处理；只升级内置默认项，不覆盖用户自定义规则。
                if (settings.NamingPresetVersion < 7)
                {
                    var rules = settings.MachinedMaterialProcessRules ?? string.Empty;
                    rules = rules.Replace("6061=6061-cnc", "6061=6061|cnc|本色氧化")
                        .Replace("5052=5052-钣金", "5052=5052|钣金|白色细砂纹烤漆")
                        .Replace("304=304-cnc", "304=304|cnc|")
                        .Replace("轴304=304-车铣", "轴304=304|车铣|")
                        .Replace("淘宝=--追加工", "淘宝=-|追加工|");
                    settings.MachinedMaterialProcessRules = rules;
                    settings.NamingPresetVersion = 7;
                }

                // v8：加工件命名统一为五段，移除旧“拓展代号”段，
                // 第五段明确作为安装说明使用。
                if (settings.NamingPresetVersion < 8)
                {
                    settings.MachinedSegments = "date,material,name,serial,custom";
                    settings.MachinedSegmentLabels = "||||安装说明";
                    settings.MachinedSegmentBomNameFlags = "0|0|1|1|1";
                    settings.NamingPresetVersion = 8;
                }

                // v9：BOM 加工件字段改为按命名含义绑定，不再依赖容易失效的固定段号。
                if (settings.NamingPresetVersion < 9)
                {
                    var segments = NamingOptionsFactory.ParseMachinedSegments(settings.MachinedSegments);
                    var labels = NamingOptionsFactory.ParseMachinedSegmentLabels(
                        settings.MachinedSegmentLabels, segments.Length);
                    settings.BomMachinedNameField = MigrateMachinedFieldSource(
                        settings.BomMachinedNameField, segments, labels, true);
                    settings.BomMachinedMaterialField = MigrateMachinedFieldSource(
                        settings.BomMachinedMaterialField, segments, labels, false);
                    settings.BomMachinedProcessField = MigrateMachinedFieldSource(
                        settings.BomMachinedProcessField, segments, labels, false);
                    settings.BomMachinedSurfaceField = MigrateMachinedFieldSource(
                        settings.BomMachinedSurfaceField, segments, labels, false);
                    settings.BomMachinedAssemblyNoteField = MigrateMachinedFieldSource(
                        settings.BomMachinedAssemblyNoteField, segments, labels, false);
                    settings.BomMachinedRemarkField = MigrateMachinedFieldSource(
                        settings.BomMachinedRemarkField, segments, labels, false);
                    settings.NamingPresetVersion = 9;
                }

                // v10：加工件增加二级/三级快捷字段；标准件沿用一级前缀/二级名称。
                if (settings.NamingPresetVersion < 10)
                {
                    if (!settings._values.ContainsKey("MachinedLevel2Values"))
                    {
                        settings.MachinedLevel2Values = "6061 5052 304 轴304";
                    }
                    if (!settings._values.ContainsKey("MachinedLevel3Values"))
                    {
                        settings.MachinedLevel3Values = string.Empty;
                    }
                    settings.NamingPresetVersion = 10;
                }

                // v11：标准件字段可单独选择是否显示为选项卡快捷按钮。
                // 升级时默认全部勾选，以保持原有选项卡显示效果。
                if (settings.NamingPresetVersion < 11)
                {
                    if (!settings._values.ContainsKey("StandardTabPrefixes"))
                    {
                        settings.StandardTabPrefixes = settings.BomPrefixes;
                    }
                    if (!settings._values.ContainsKey("StandardTabMiddleNames"))
                    {
                        settings.StandardTabMiddleNames = settings.BomMiddleNames;
                    }
                    settings.NamingPresetVersion = 11;
                }

                // v12：加工件二级/三级字段可单独选择是否显示为选项卡快捷按钮。
                // 升级时默认全部勾选，保持用户升级前已经显示的快捷字段。
                if (settings.NamingPresetVersion < 12)
                {
                    if (!settings._values.ContainsKey("MachinedTabLevel2Values"))
                    {
                        settings.MachinedTabLevel2Values = settings.MachinedLevel2Values;
                    }
                    if (!settings._values.ContainsKey("MachinedTabLevel3Values"))
                    {
                        settings.MachinedTabLevel3Values = settings.MachinedLevel3Values;
                    }
                    settings.NamingPresetVersion = 12;
                }

                // v13：标准件 BOM 字段按“型号、留空、中间名、留空、自动、自动”的顺序输出。
                // 统一迁移旧配置，确保已安装版本升级后也立即采用新的标准件表格布局。
                if (settings.NamingPresetVersion < 13)
                {
                    settings.BomStandardNameField = "tail:3";
                    settings.BomStandardMaterialField = "empty";
                    settings.BomStandardProcessField = "segment:2";
                    settings.BomStandardSurfaceField = "empty";
                    settings.BomStandardAssemblyNoteField = "auto";
                    settings.BomStandardRemarkField = "auto";
                    settings.NamingPresetVersion = 13;
                }

                // v14：在“位置”和“属性”之间增加完整名称列；旧配置也按该位置插入。
                if (settings.NamingPresetVersion < 14)
                {
                    var order = new List<string>((settings.BomColumnOrder ?? string.Empty)
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                    order.RemoveAll(delegate(string value)
                    {
                        return string.Equals(value.Trim(), "fullname",
                            StringComparison.OrdinalIgnoreCase);
                    });
                    var locationIndex = order.FindIndex(delegate(string value)
                    {
                        return string.Equals(value.Trim(), "location",
                            StringComparison.OrdinalIgnoreCase);
                    });
                    order.Insert(locationIndex < 0 ? 0 : locationIndex + 1, "fullname");
                    settings.BomColumnOrder = string.Join(",", order.ToArray());
                    settings.BomFullNameHeader = "完整名称";
                    settings.NamingPresetVersion = 14;
                }

                // v15：「完整名称」改名「三维名称」，并在它后面增加「二维工程图」列。
                if (settings.NamingPresetVersion < 15)
                {
                    if (string.Equals((settings.BomFullNameHeader ?? string.Empty).Trim(), "完整名称",
                            StringComparison.Ordinal))
                    {
                        settings.BomFullNameHeader = "三维名称";
                    }

                    var order = new List<string>((settings.BomColumnOrder ?? string.Empty)
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                    var hasDrawing = order.Exists(delegate(string value)
                    {
                        return string.Equals(value.Trim(), "drawing", StringComparison.OrdinalIgnoreCase);
                    });
                    if (!hasDrawing)
                    {
                        var fullNameIndex = order.FindIndex(delegate(string value)
                        {
                            return string.Equals(value.Trim(), "fullname", StringComparison.OrdinalIgnoreCase);
                        });
                        order.Insert(fullNameIndex < 0 ? order.Count : fullNameIndex + 1, "drawing");
                    }
                    settings.BomColumnOrder = string.Join(",", order.ToArray());
                    settings.NamingPresetVersion = 15;
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取配置失败", ex);
            }

            return settings;
        }

        private static string MigrateMachinedFieldSource(string source,
            MachinedSegmentKind[] segments, string[] labels, bool nameField)
        {
            var value = (source ?? string.Empty).Trim();
            if (!value.StartsWith("segment:", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            int number;
            if (!int.TryParse(value.Substring("segment:".Length), out number) || number < 1 ||
                segments == null || number > segments.Length)
            {
                return "auto";
            }

            var kind = segments[number - 1];
            if (nameField && kind == MachinedSegmentKind.Name)
            {
                // 旧逻辑的默认“第 3 段”实际会被组合名称覆盖；迁移为自动可保持原结果。
                return "auto";
            }

            var label = labels != null && number <= labels.Length ? labels[number - 1] : string.Empty;
            return NamingOptionsFactory.MachinedSegmentFieldCode(kind, label);
        }

        public void Save()
        {
            try
            {
                AppPaths.Ensure(AppPaths.Root);
                WriteFile(AppPaths.SettingsFile, _values, "# MechKit 本机配置");
            }
            catch (Exception ex)
            {
                Log.Error("保存配置失败", ex);
            }
        }

        /// <summary>
        /// 导出可跨电脑使用的 MechKit 功能配置与命名预设。
        /// 窗口位置、输出目录和 SOLIDWORKS 本机路径不会写入。
        /// </summary>
        public bool ExportPortable(string targetFile, out string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(targetFile))
                {
                    message = "没有指定配置文件路径。";
                    return false;
                }

                var directory = Path.GetDirectoryName(Path.GetFullPath(targetFile));
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var portableValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in _values)
                {
                    if (IsPortableKey(pair.Key))
                    {
                        portableValues[pair.Key] = pair.Value;
                    }
                }

                WriteFile(targetFile, portableValues,
                    "# MechKit 便携配置：复制到 MechKit.dll 所在目录，重启 SOLIDWORKS 自动生效");
                message = "MechKit 配置与预设已导出：" + targetFile;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("导出便携配置失败", ex);
                message = "导出 MechKit 配置失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>把便携配置用固定文件名直接导出到当前用户桌面。</summary>
        public bool ExportPortableToDesktop(out string targetFile, out string message)
        {
            targetFile = string.Empty;
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop))
            {
                message = "无法获取当前用户的桌面目录。";
                return false;
            }

            targetFile = Path.Combine(desktop, PortableFileName);
            return ExportPortable(targetFile, out message);
        }

        private static void LoadFile(AddinSettings settings, string file)
        {
            if (settings == null || string.IsNullOrEmpty(file) || !File.Exists(file))
            {
                return;
            }

            foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var index = line.IndexOf('=');
                if (index <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, index).Trim();
                var value = line.Substring(index + 1);
                settings._values[key] = Unescape(value);
            }
        }

        private static void WriteFile(string file, IDictionary<string, string> values, string heading)
        {
            var builder = new StringBuilder();
            builder.AppendLine(heading);
            foreach (var pair in values)
            {
                builder.Append(pair.Key).Append('=').AppendLine(Escape(pair.Value));
            }

            File.WriteAllText(file, builder.ToString(), new UTF8Encoding(true));
        }

        private static bool IsPortableKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.StartsWith("Window.", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            switch (key.ToLowerInvariant())
            {
                case "sourcefolder":
                case "outputfolder":
                case "propertyfolder":
                case "weldmentfolder":
                case "weldmentlibrarysource":
                case "templatefolder":
                case "macrofolder":
                case "toolboxfolder":
                case "settingsbackupfolder":
                    return false;
                default:
                    return true;
            }
        }

        public string Get(string key, string fallback)
        {
            string value;
            return _values.TryGetValue(key, out value) ? value : fallback;
        }

        public void Set(string key, string value)
        {
            _values[key] = value ?? string.Empty;
        }

        private bool GetBool(string key, bool fallback)
        {
            var value = Get(key, null);
            if (string.IsNullOrEmpty(value))
            {
                return fallback;
            }

            return string.Equals(value, "1", StringComparison.Ordinal) ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private void SetBool(string key, bool value)
        {
            Set(key, value ? "1" : "0");
        }

        private int GetInt(string key, int fallback)
        {
            var value = Get(key, null);
            int parsed;
            return !string.IsNullOrEmpty(value) && int.TryParse(value,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : fallback;
        }

        private void SetInt(string key, int value)
        {
            Set(key, value.ToString(CultureInfo.InvariantCulture));
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        private static string Unescape(string value)
        {
            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length)
                {
                    i++;
                    switch (value[i])
                    {
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        default:
                            builder.Append(value[i]);
                            break;
                    }
                }
                else
                {
                    builder.Append(value[i]);
                }
            }

            return builder.ToString();
        }
    }
}
