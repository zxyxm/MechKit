using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MechKit.Core
{
    /// <summary>
    /// 轻量配置存储（key=value 文本），避免引用第三方 JSON 库。
    /// </summary>
    public sealed class AddinSettings
    {
        private readonly Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

        /// <summary>是否按分隔符把文件名分段解析名称/材料（日期_材料_名称）。</summary>
        public bool UseNameSegments
        {
            get { return GetBool("UseNameSegments", true); }
            set { SetBool("UseNameSegments", value); }
        }

        public string SegmentSeparator
        {
            get { return Get("SegmentSeparator", "_-"); }
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
            get { return Get("MachinedSegmentLabels", "||||拓展代号"); }
            set { Set("MachinedSegmentLabels", value); }
        }

        /// <summary>加工件第2段到“材料,工艺”的预设映射；每行一条 key=材料,工艺。</summary>
        public string MachinedMaterialProcessRules
        {
            get
            {
                return Get("MachinedMaterialProcessRules",
                    "6061=6061,cnc\n5052=5052,钣金\n304=304,cnc\n轴304=304,车铣\n淘宝=-,追加工");
            }
            set { Set("MachinedMaterialProcessRules", value); }
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
            get { return Get("BomMiddleNames", "接近开关 电机 丝杆"); }
            set { Set("BomMiddleNames", value); }
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
            get { return Get("StandardPrefixBindings", "电机=代理|接近开关=代理"); }
            set { Set("StandardPrefixBindings", value); }
        }

        public int NamingPresetVersion
        {
            get { return GetInt("NamingPresetVersion", 0); }
            set { SetInt("NamingPresetVersion", value); }
        }

        /// <summary>是否只收录「前缀_日期_材料_名称」这种命名的零件。</summary>
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

        // BOM 可编辑列的取值来源：auto / whole / segment:1..8 / tail:3 /
        // property:name|material|process|remark / empty。
        public string BomStandardNameField
        {
            get { return Get("BomStandardNameField", "segment:2"); }
            set { Set("BomStandardNameField", value); }
        }

        public string BomStandardMaterialField
        {
            get { return Get("BomStandardMaterialField", "tail:3"); }
            set { Set("BomStandardMaterialField", value); }
        }

        public string BomStandardProcessField
        {
            get { return Get("BomStandardProcessField", "segment:1"); }
            set { Set("BomStandardProcessField", value); }
        }

        public string BomStandardRemarkField
        {
            get { return Get("BomStandardRemarkField", "property:remark"); }
            set { Set("BomStandardRemarkField", value); }
        }

        public string BomMachinedNameField
        {
            get { return Get("BomMachinedNameField", "segment:3"); }
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
                var file = AppPaths.SettingsFile;
                if (!File.Exists(file))
                {
                    return settings;
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
                    settings.SegmentSeparator = "_-";
                    settings.MachinedSegments = "date,material,name,serial,custom";
                    settings.MachinedSegmentLabels = "||||拓展代号";
                    settings.MachinedMaterialProcessRules =
                        "6061=6061,cnc\n5052=5052,钣金\n304=304,cnc\n轴304=304,车铣\n淘宝=-,追加工";
                    settings.BomPrefixes = "淘宝 代理 淘宝追加工";
                    settings.BomMiddleNames = "接近开关 电机 丝杆";
                    settings.StandardPrefixBindingEnabled = true;
                    settings.StandardPrefixBindings = "电机=代理|接近开关=代理";
                    settings.BomMachinedMaterialField = "auto";
                    settings.BomMachinedProcessField = "auto";
                    settings.NamingPresetVersion = 1;
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取配置失败", ex);
            }

            return settings;
        }

        public void Save()
        {
            try
            {
                AppPaths.Ensure(AppPaths.Root);

                var builder = new StringBuilder();
                builder.AppendLine("# MechKit 配置");
                foreach (var pair in _values)
                {
                    builder.Append(pair.Key).Append('=').AppendLine(Escape(pair.Value));
                }

                File.WriteAllText(AppPaths.SettingsFile, builder.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Error("保存配置失败", ex);
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
