using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace MechKit.Core
{
    /// <summary>图号取值来源。</summary>
    internal enum PartNumberSource
    {
        /// <summary>直接用零件文件名。</summary>
        FileName = 0,

        /// <summary>优先读自定义属性，取不到再退回文件名。</summary>
        PropertyFirst = 1,

        /// <summary>只认自定义属性。</summary>
        PropertyOnly = 2
    }

    /// <summary>文件名截断规则。</summary>
    internal enum FileNameCutRule
    {
        /// <summary>整个文件名。</summary>
        None = 0,

        /// <summary>第一个空格之前。</summary>
        FirstSpace = 1,

        /// <summary>第一个下划线之前。</summary>
        FirstUnderscore = 2,

        /// <summary>第一个短横线之前。</summary>
        FirstHyphen = 3,

        /// <summary>自定义正则。</summary>
        Regex = 4
    }

    /// <summary>材料取值来源。</summary>
    internal enum MaterialSource
    {
        /// <summary>SOLIDWORKS 中指定的材料。</summary>
        Model = 0,

        /// <summary>优先读自定义属性，取不到再用模型材料。</summary>
        PropertyFirst = 1,

        /// <summary>只认自定义属性。</summary>
        PropertyOnly = 2
    }

    /// <summary>加工件文件名中各段的用途；数组顺序就是文件名中的前后顺序。</summary>
    internal enum MachinedSegmentKind
    {
        Date = 0,
        Material = 1,
        Name = 2,
        Version = 3,
        AssemblyNote = 4,
        Custom = 5
    }

    internal sealed class MaterialProcessPreset
    {
        public string Material { get; set; }
        public string Process { get; set; }
    }

    /// <summary>
    /// 图号 / 名称 / 材料的解析规则。
    /// 机械制图习惯：文件名形如「JX-2024-001 支架」，图号取空格前，名称取空格后。
    /// </summary>
    internal sealed class NamingOptions
    {
        public NamingOptions()
        {
            Source = PartNumberSource.FileName;
            Cut = FileNameCutRule.FirstSpace;
            Pattern = string.Empty;
            Material = MaterialSource.Model;
            UseNameSegments = false;
            SegmentSeparator = "_-";
            NameSegment = -1;
            MaterialSegment = -2;
            MachinedSegments = new[]
            {
                MachinedSegmentKind.Date,
                MachinedSegmentKind.Material,
                MachinedSegmentKind.Name,
                MachinedSegmentKind.Version,
                MachinedSegmentKind.AssemblyNote
            };
            MachinedSegmentLabels = new[] { string.Empty, string.Empty, string.Empty, string.Empty, string.Empty };
            MachinedSegmentBomNameFlags = new[] { false, false, true, false, false };
            MachinedMaterialProcessPresets = new Dictionary<string, MaterialProcessPreset>(StringComparer.OrdinalIgnoreCase);
            PartNumberProperties = new[] { "图号", "零件号", "零件代号", "代号", "PartNumber", "Part Number", "Number", "DrawingNo" };
            NameProperties = new[] { "名称", "零件名称", "Description", "Title", "Name" };
            MaterialProperties = new[] { "材料", "材质", "Material", "材质牌号" };
        }

        public PartNumberSource Source { get; set; }

        public FileNameCutRule Cut { get; set; }

        public string Pattern { get; set; }

        public MaterialSource Material { get; set; }

        /// <summary>
        /// 按分隔符把文件名分段解析名称与材料，例如「20260908_6061_扫码枪安装板」
        /// 得到 材料 = 6061、名称 = 扫码枪安装板。适合「日期_材料_名称」这类命名习惯。
        /// </summary>
        public bool UseNameSegments { get; set; }

        public string SegmentSeparator { get; set; }

        /// <summary>名称所在段：1 起算的正数，或负数表示从末尾数（-1 = 最后一段）。</summary>
        public int NameSegment { get; set; }

        /// <summary>材料所在段：-2 = 倒数第二段，0 = 不从文件名取材料。</summary>
        public int MaterialSegment { get; set; }

        /// <summary>加工件段定义；顺序对应文件名中从左到右的位置。</summary>
        public MachinedSegmentKind[] MachinedSegments { get; set; }

        /// <summary>加工件段的自定义显示名称；与 MachinedSegments 下标一致。</summary>
        public string[] MachinedSegmentLabels { get; set; }

        /// <summary>每个加工件段是否并入 BOM“零件名”列；与 MachinedSegments 下标一致。</summary>
        public bool[] MachinedSegmentBomNameFlags { get; set; }

        /// <summary>加工件材料段到材料/工艺的映射。</summary>
        public Dictionary<string, MaterialProcessPreset> MachinedMaterialProcessPresets { get; set; }

        /// <summary>BOM 收录用的名称前缀，例如 电机 / 电气 / 淘宝。用下划线分隔各段。</summary>
        public string[] BomPrefixes { get; set; }

        /// <summary>
        /// true = 只收录两类零件：
        ///   加工件：名称以日期开头，例如 20260908_6061_扫码枪安装板
        ///   标准件：名称以已知前缀开头，例如 电机_、电气_、淘宝_
        /// 其余（标准件/焊件的子零件）不进入 BOM。
        /// </summary>
        public bool RequireBomPattern { get; set; }

        /// <summary>判断名称是否属于「加工件（日期开头）」或「标准件（前缀开头）」，即是否进入 BOM。</summary>
        public bool MatchesBomPattern(string name)
        {
            if (!RequireBomPattern)
            {
                return true;
            }

            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var fileName = GetFileNameWithoutExtension(name).Trim();
            return IsMachinedName(fileName) || HasKnownPrefix(FirstSegment(fileName));
        }

        /// <summary>按当前段顺序找到时间段，并用它判断是否为加工件。</summary>
        public bool IsMachinedName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            var cleanName = GetFileNameWithoutExtension(fileName).Trim();
            var dateIndex = IndexOfSegment(MachinedSegmentKind.Date);
            if (dateIndex < 0 || string.IsNullOrEmpty(SegmentSeparator))
            {
                return StartsWithDate(cleanName);
            }

            var parts = SplitSegments(cleanName);
            return dateIndex < parts.Length && IsDateSegment(parts[dateIndex]);
        }

        /// <summary>名称是否以日期开头（6~8 位数字，后面跟分隔符或直接结束）。</summary>
        public static bool StartsWithDate(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            var digits = 0;
            while (digits < fileName.Length && char.IsDigit(fileName[digits]))
            {
                digits++;
            }

            if (digits < 6)
            {
                return false;
            }

            if (digits == fileName.Length)
            {
                return true;
            }

            var separator = fileName[digits];
            return separator == '_' || separator == '-' || separator == ' ';
        }

        private static bool IsDateSegment(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            var text = value.Trim();
            if (text.Length < 6 || text.Length > 8)
            {
                return false;
            }

            foreach (var character in text)
            {
                if (!char.IsDigit(character))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>取名称的第一段（下划线分隔）。</summary>
        public static string FirstSegment(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return string.Empty;
            }

            var index = fileName.IndexOf('_');
            return (index < 0 ? fileName : fileName.Substring(0, index)).Trim();
        }

        /// <summary>名称是否已经带有已知前缀。</summary>
        public bool HasKnownPrefix(string segment)
        {
            if (string.IsNullOrEmpty(segment) || BomPrefixes == null)
            {
                return false;
            }

            var value = segment.Trim();
            foreach (var prefix in BomPrefixes)
            {
                if (!string.IsNullOrEmpty(prefix) &&
                    string.Equals(value, prefix.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public string[] PartNumberProperties { get; set; }

        public string[] NameProperties { get; set; }

        public string[] MaterialProperties { get; set; }

        /// <summary>图号：先按来源取原始文本，再按截断规则处理。</summary>
        public string ResolvePartNumber(string filePath, Func<string, string> propertyLookup)
        {
            var fileName = GetFileNameWithoutExtension(filePath);

            if (Source != PartNumberSource.FileName)
            {
                var fromProperty = Lookup(propertyLookup, PartNumberProperties);
                if (!string.IsNullOrEmpty(fromProperty))
                {
                    return fromProperty.Trim();
                }
            }

            return ApplyCut(fileName);
        }

        /// <summary>名称：自定义属性优先，其次用文件名截断后的剩余部分，最后退回完整文件名。</summary>
        public string ResolveName(string filePath, Func<string, string> propertyLookup)
        {
            var fromProperty = Lookup(propertyLookup, NameProperties);
            if (!string.IsNullOrEmpty(fromProperty))
            {
                return fromProperty.Trim();
            }

            var fileName = GetFileNameWithoutExtension(filePath);

            var configuredNameSegment = IndexOfSegment(MachinedSegmentKind.Name);
            var fromSegment = UseNameSegments && IsMachinedName(fileName) && configuredNameSegment >= 0
                ? ResolveMachinedBomName(fileName)
                : PickSegment(fileName, NameSegment);
            if (UseNameSegments && !string.IsNullOrEmpty(fromSegment))
            {
                return fromSegment;
            }

            var cut = ApplyCut(fileName);
            var remainder = GetRemainder(fileName);

            if (!string.IsNullOrEmpty(remainder) && !string.Equals(cut, fileName, StringComparison.Ordinal))
            {
                return remainder;
            }

            return fileName;
        }

        public string ResolveMaterial(string modelMaterial, Func<string, string> propertyLookup)
        {
            var fromProperty = Lookup(propertyLookup, MaterialProperties);

            switch (Material)
            {
                case MaterialSource.PropertyOnly:
                    return string.IsNullOrEmpty(fromProperty) ? string.Empty : fromProperty.Trim();
                case MaterialSource.PropertyFirst:
                    return !string.IsNullOrEmpty(fromProperty)
                        ? fromProperty.Trim()
                        : (modelMaterial ?? string.Empty).Trim();
                default:
                    return !string.IsNullOrEmpty(modelMaterial)
                        ? modelMaterial.Trim()
                        : (fromProperty ?? string.Empty).Trim();
            }
        }

        /// <summary>从文件名分段取材料；未启用或取不到时返回空字符串。</summary>
        public string ResolveMaterialFromSegments(string filePath)
        {
            if (!UseNameSegments)
            {
                return string.Empty;
            }

            var fileName = GetFileNameWithoutExtension(filePath);
            var configuredMaterialSegment = IndexOfSegment(MachinedSegmentKind.Material);
            if (IsMachinedName(fileName) && configuredMaterialSegment >= 0)
            {
                return PickSegment(fileName, configuredMaterialSegment + 1);
            }

            return MaterialSegment == 0 ? string.Empty : PickSegment(fileName, MaterialSegment);
        }

        /// <summary>严格按加工件段定义从文件名取零件名称，不读取自定义属性。</summary>
        public string ResolveNameFromSegments(string filePath)
        {
            if (!UseNameSegments)
            {
                return string.Empty;
            }

            var fileName = GetFileNameWithoutExtension(filePath);
            var configuredNameSegment = IndexOfSegment(MachinedSegmentKind.Name);
            return IsMachinedName(fileName) && configuredNameSegment >= 0
                ? ResolveMachinedBomName(fileName)
                : string.Empty;
        }

        /// <summary>是否选择了名称段以外的字段并入 BOM 零件名。</summary>
        public bool UsesCompositeMachinedBomName()
        {
            if (MachinedSegments == null || MachinedSegmentBomNameFlags == null ||
                MachinedSegments.Length != MachinedSegmentBomNameFlags.Length)
            {
                return false;
            }

            for (var i = 0; i < MachinedSegments.Length; i++)
            {
                if (MachinedSegmentBomNameFlags[i] && MachinedSegments[i] != MachinedSegmentKind.Name)
                {
                    return true;
                }
            }

            return false;
        }

        private string ResolveMachinedBomName(string fileName)
        {
            var parts = SplitSegments(fileName);
            var values = new List<string>();
            var hasExplicitFlags = MachinedSegments != null && MachinedSegmentBomNameFlags != null &&
                                   MachinedSegments.Length == MachinedSegmentBomNameFlags.Length;

            if (MachinedSegments != null)
            {
                for (var i = 0; i < MachinedSegments.Length && i < parts.Length; i++)
                {
                    var include = hasExplicitFlags
                        ? MachinedSegmentBomNameFlags[i]
                        : MachinedSegments[i] == MachinedSegmentKind.Name;
                    var value = parts[i].Trim();
                    if (include && !IsPlaceholder(value))
                    {
                        values.Add(value);
                    }
                }
            }

            if (values.Count > 0)
            {
                return string.Join("_", values.ToArray());
            }

            var nameIndex = IndexOfSegment(MachinedSegmentKind.Name);
            return nameIndex >= 0 ? PickSegment(fileName, nameIndex + 1) : string.Empty;
        }

        /// <summary>按加工件第2段预设同时解析材料和工艺。</summary>
        public bool TryResolveMachinedMaterialProcess(string filePath, out string material, out string process)
        {
            material = string.Empty;
            process = string.Empty;
            if (!UseNameSegments || MachinedMaterialProcessPresets == null)
            {
                return false;
            }

            var fileName = GetFileNameWithoutExtension(filePath);
            var configuredMaterialSegment = IndexOfSegment(MachinedSegmentKind.Material);
            if (!IsMachinedName(fileName) || configuredMaterialSegment < 0)
            {
                return false;
            }

            var token = PickSegment(fileName, configuredMaterialSegment + 1);
            MaterialProcessPreset preset;
            if (string.IsNullOrEmpty(token) || !MachinedMaterialProcessPresets.TryGetValue(token, out preset))
            {
                return false;
            }

            material = preset.Material ?? string.Empty;
            process = preset.Process ?? string.Empty;
            return true;
        }

        private int IndexOfSegment(MachinedSegmentKind kind)
        {
            if (MachinedSegments == null)
            {
                return -1;
            }

            for (var i = 0; i < MachinedSegments.Length; i++)
            {
                if (MachinedSegments[i] == kind)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 判断属性值是否为模板占位符。SOLIDWORKS 中文模板会把未填写的标题栏
        /// 属性留成「“图样名称”」「材质 <未指定>」这类文字，必须当成空值。
        /// </summary>
        public static bool IsPlaceholder(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return true;
            }

            var text = value.Trim();
            if (text.Length == 0)
            {
                return true;
            }

            if (text.Contains("图样名称") || text.Contains("图样代号") || text.Contains("图样材料"))
            {
                return true;
            }

            if (text.Contains("未指定"))
            {
                return true;
            }

            return false;
        }

        private string PickSegment(string fileName, int segment)
        {
            if (string.IsNullOrEmpty(fileName) || segment == 0 || string.IsNullOrEmpty(SegmentSeparator))
            {
                return string.Empty;
            }

            var parts = SplitSegments(fileName);
            var index = segment > 0 ? segment - 1 : parts.Length + segment;

            if (index < 0 || index >= parts.Length)
            {
                return string.Empty;
            }

            var value = parts[index].Trim();
            return IsPlaceholder(value) ? string.Empty : value;
        }

        /// <summary>
        /// 加工件允许整份名称使用 _ 或 -。以日期后的第一个分隔符为准，
        /// 避免型号/零件名内部自带短横线时被误拆成额外字段。
        /// </summary>
        private string[] SplitSegments(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return new string[0];
            }

            var separator = string.IsNullOrEmpty(SegmentSeparator) ? '_' : SegmentSeparator[0];
            var digits = 0;
            while (digits < fileName.Length && char.IsDigit(fileName[digits]))
            {
                digits++;
            }
            if (digits >= 6 && digits < fileName.Length &&
                (fileName[digits] == '_' || fileName[digits] == '-'))
            {
                separator = fileName[digits];
            }

            return fileName.Split(new[] { separator }, StringSplitOptions.None);
        }

        /// <summary>按规则截断文件名，得到图号。</summary>
        public string ApplyCut(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return string.Empty;
            }

            switch (Cut)
            {
                case FileNameCutRule.FirstSpace:
                    return Trim(FirstSegment(fileName, new[] { ' ', '\t', '　' }));
                case FileNameCutRule.FirstUnderscore:
                    return Trim(FirstSegment(fileName, new[] { '_' }));
                case FileNameCutRule.FirstHyphen:
                    return Trim(FirstSegment(fileName, new[] { '-' }));
                case FileNameCutRule.Regex:
                {
                    if (string.IsNullOrEmpty(Pattern))
                    {
                        return Trim(fileName);
                    }

                    try
                    {
                        var match = Regex.Match(fileName, Pattern);
                        if (!match.Success)
                        {
                            return Trim(fileName);
                        }

                        if (match.Groups.Count > 1 && match.Groups[1].Success)
                        {
                            return Trim(match.Groups[1].Value);
                        }

                        return Trim(match.Value);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("图号正则无效，已退回完整文件名：" + ex.Message);
                        return Trim(fileName);
                    }
                }
                default:
                    return Trim(fileName);
            }
        }

        /// <summary>截断后剩下的部分，用作零件名称。</summary>
        public string GetRemainder(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || Cut == FileNameCutRule.None || Cut == FileNameCutRule.Regex)
            {
                return string.Empty;
            }

            var separators = Cut == FileNameCutRule.FirstSpace
                ? new[] { ' ', '\t', '　' }
                : (Cut == FileNameCutRule.FirstUnderscore ? new[] { '_' } : new[] { '-' });

            var index = fileName.IndexOfAny(separators);
            if (index < 0 || index >= fileName.Length - 1)
            {
                return string.Empty;
            }

            return fileName.Substring(index + 1).Trim();
        }

        public string Describe()
        {
            string source;
            switch (Source)
            {
                case PartNumberSource.PropertyFirst:
                    source = "自定义属性优先";
                    break;
                case PartNumberSource.PropertyOnly:
                    source = "仅自定义属性";
                    break;
                default:
                    source = "文件名";
                    break;
            }

            string cut;
            switch (Cut)
            {
                case FileNameCutRule.FirstSpace:
                    cut = "空格前";
                    break;
                case FileNameCutRule.FirstUnderscore:
                    cut = "下划线前";
                    break;
                case FileNameCutRule.FirstHyphen:
                    cut = "短横线前";
                    break;
                case FileNameCutRule.Regex:
                    cut = "正则 " + Pattern;
                    break;
                default:
                    cut = "完整文件名";
                    break;
            }

            var segments = UseNameSegments
                ? string.Format("；按「{0}」分段（{1}）",
                    SegmentSeparator, NamingOptionsFactory.DescribeMachinedSegments(
                        MachinedSegments, MachinedSegmentLabels))
                : string.Empty;

            return string.Format("图号：{0} / {1}{2}", source, cut, segments);
        }

        public static string GetFileNameWithoutExtension(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFileNameWithoutExtension(filePath);
            }
            catch
            {
                return filePath;
            }
        }

        /// <summary>把 SOLIDWORKS 的 MaterialIdName（形如 "库.sldmat|材料名"）转成可读材料名。</summary>
        public static string NormalizeMaterialId(string materialId)
        {
            if (string.IsNullOrEmpty(materialId))
            {
                return string.Empty;
            }

            var index = materialId.LastIndexOf('|');
            return (index >= 0 ? materialId.Substring(index + 1) : materialId).Trim();
        }

        private static string Lookup(Func<string, string> propertyLookup, IList<string> names)
        {
            if (propertyLookup == null || names == null)
            {
                return string.Empty;
            }

            foreach (var name in names)
            {
                try
                {
                    var value = propertyLookup(name);
                    if (!IsPlaceholder(value))
                    {
                        return value;
                    }
                }
                catch
                {
                    // 单个属性读取失败不影响整体
                }
            }

            return string.Empty;
        }

        private static string FirstSegment(string value, char[] separators)
        {
            var index = value.IndexOfAny(separators);
            return index < 0 ? value : value.Substring(0, index);
        }

        private static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim().TrimEnd('-', '_', '.', ' ');
        }
    }
}
