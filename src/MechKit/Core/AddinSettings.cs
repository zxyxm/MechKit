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
            get { return GetBool("UseNameSegments", false); }
            set { SetBool("UseNameSegments", value); }
        }

        public string SegmentSeparator
        {
            get { return Get("SegmentSeparator", "_"); }
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

        public bool PartListOnlyMachined
        {
            get { return GetBool("PartListOnlyMachined", true); }
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

        /// <summary>BOM 收录用的名称前缀，逗号分隔，例如：电机,电气,淘宝。</summary>
        public string BomPrefixes
        {
            get { return Get("BomPrefixes", "电机,电气,淘宝"); }
            set { Set("BomPrefixes", value); }
        }

        /// <summary>是否只收录「前缀_日期_材料_名称」这种命名的零件。</summary>
        public bool BomRequirePattern
        {
            get { return GetBool("BomRequirePattern", true); }
            set { SetBool("BomRequirePattern", value); }
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
