using System;
using System.Collections.Generic;

namespace MechKit.Core
{
    /// <summary>把用户保存的设置转换成命名规则对象，保证各处规则完全一致。</summary>
    internal static class NamingOptionsFactory
    {
        public static NamingOptions FromSettings(AddinSettings settings)
        {
            var naming = new NamingOptions();
            if (settings == null)
            {
                return naming;
            }

            naming.Source = (PartNumberSource)settings.PartNumberSource;
            naming.Cut = (FileNameCutRule)settings.PartNumberCutRule;
            naming.Pattern = settings.PartNumberPattern;
            naming.Material = (MaterialSource)settings.MaterialSource;
            naming.UseNameSegments = true;
            // BOM 字段统一以单个下划线分隔，避免不同电脑配置造成列错位。
            naming.SegmentSeparator = "_";
            naming.NameSegment = -1;
            naming.MaterialSegment = settings.MaterialSegment;
            naming.MachinedSegments = ParseMachinedSegments(settings.MachinedSegments);
            naming.MachinedSegmentLabels = ParseMachinedSegmentLabels(
                settings.MachinedSegmentLabels, naming.MachinedSegments.Length);
            naming.BomPrefixes = ParsePrefixes(settings.BomPrefixes);
            naming.RequireBomPattern = settings.BomRequirePattern;
            return naming;
        }

        public static MachinedSegmentKind[] ParseMachinedSegments(string text)
        {
            var result = new List<MachinedSegmentKind>();
            foreach (var token in (text ?? string.Empty).Split(','))
            {
                MachinedSegmentKind kind;
                switch (token.Trim().ToLowerInvariant())
                {
                    case "date": kind = MachinedSegmentKind.Date; break;
                    case "material": kind = MachinedSegmentKind.Material; break;
                    case "name": kind = MachinedSegmentKind.Name; break;
                    case "serial": kind = MachinedSegmentKind.Serial; break;
                    case "custom": kind = MachinedSegmentKind.Custom; break;
                    default: continue;
                }

                result.Add(kind);
            }

            if (result.Count == 0)
            {
                result.Add(MachinedSegmentKind.Date);
                result.Add(MachinedSegmentKind.Material);
                result.Add(MachinedSegmentKind.Name);
                result.Add(MachinedSegmentKind.Serial);
            }

            // 时间段是加工件规则的固定锚点：无论旧配置里位于何处、重复几次或缺失，
            // 载入后都只保留一个，并强制放在第 1 段。
            result.RemoveAll(delegate(MachinedSegmentKind kind)
            {
                return kind == MachinedSegmentKind.Date;
            });
            result.Insert(0, MachinedSegmentKind.Date);
            return result.ToArray();
        }

        public static string SerializeMachinedSegments(IEnumerable<MachinedSegmentKind> segments)
        {
            var result = new List<string>();
            if (segments != null)
            {
                foreach (var segment in segments)
                {
                    result.Add(segment.ToString().ToLowerInvariant());
                }
            }

            return string.Join(",", result.ToArray());
        }

        public static string DescribeMachinedSegments(IEnumerable<MachinedSegmentKind> segments)
        {
            return DescribeMachinedSegments(segments, null);
        }

        public static string DescribeMachinedSegments(IEnumerable<MachinedSegmentKind> segments,
            IList<string> labels)
        {
            var result = new List<string>();
            var index = 0;
            if (segments != null)
            {
                foreach (var segment in segments)
                {
                    switch (segment)
                    {
                        case MachinedSegmentKind.Date: result.Add("时间"); break;
                        case MachinedSegmentKind.Material: result.Add("材料"); break;
                        case MachinedSegmentKind.Name: result.Add("零件名称"); break;
                        case MachinedSegmentKind.Serial: result.Add("扩展序号"); break;
                        default:
                            var label = labels != null && index < labels.Count
                                ? labels[index]
                                : string.Empty;
                            result.Add(string.IsNullOrWhiteSpace(label) ? "自定义" : label.Trim());
                            break;
                    }

                    index++;
                }
            }

            return string.Join(" → ", result.ToArray());
        }

        public static string[] ParseMachinedSegmentLabels(string text, int count)
        {
            var result = new List<string>();
            foreach (var token in (text ?? string.Empty).Split('|'))
            {
                try
                {
                    result.Add(Uri.UnescapeDataString(token));
                }
                catch
                {
                    result.Add(token);
                }
            }

            while (result.Count < count)
            {
                result.Add(string.Empty);
            }

            if (result.Count > count)
            {
                result.RemoveRange(count, result.Count - count);
            }

            return result.ToArray();
        }

        public static string SerializeMachinedSegmentLabels(IEnumerable<string> labels)
        {
            var result = new List<string>();
            if (labels != null)
            {
                foreach (var label in labels)
                {
                    result.Add(Uri.EscapeDataString((label ?? string.Empty).Trim()));
                }
            }

            return string.Join("|", result.ToArray());
        }

        /// <summary>解析前缀列表；界面使用空格分隔，并兼容旧的逗号 / 分号 / 顿号。</summary>
        public static string[] ParsePrefixes(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return result.ToArray();
            }

            foreach (var part in text.Split(new[]
            {
                ' ', '\t', '\r', '\n', '　', ',', '，', ';', '；', '、'
            }, StringSplitOptions.RemoveEmptyEntries))
            {
                var value = part.Trim().TrimEnd('_', '*', '＊');
                if (value.Length > 0 && !result.Contains(value))
                {
                    result.Add(value);
                }
            }

            return result.ToArray();
        }

        /// <summary>按界面约定用空格保存前缀，同时便于直接阅读和复制。</summary>
        public static string SerializePrefixes(IEnumerable<string> prefixes)
        {
            return string.Join(" ", ParsePrefixes(prefixes == null
                ? string.Empty
                : string.Join(" ", new List<string>(prefixes).ToArray())));
        }

        public static Dictionary<string, string> ParsePrefixDescriptions(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in (text ?? string.Empty).Split('|'))
            {
                var separator = entry.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                try
                {
                    var prefix = Uri.UnescapeDataString(entry.Substring(0, separator)).Trim();
                    var description = Uri.UnescapeDataString(entry.Substring(separator + 1)).Trim();
                    if (prefix.Length > 0)
                    {
                        result[prefix] = description;
                    }
                }
                catch
                {
                    // 单条旧数据损坏时忽略，不影响其余前缀。
                }
            }

            return result;
        }

        public static string SerializePrefixDescriptions(IEnumerable<string> prefixes,
            IDictionary<string, string> descriptions)
        {
            var result = new List<string>();
            if (prefixes == null)
            {
                return string.Empty;
            }

            foreach (var prefix in prefixes)
            {
                if (string.IsNullOrWhiteSpace(prefix))
                {
                    continue;
                }

                string description;
                if (descriptions == null || !descriptions.TryGetValue(prefix.Trim(), out description))
                {
                    description = string.Empty;
                }

                result.Add(Uri.EscapeDataString(prefix.Trim()) + "=" +
                           Uri.EscapeDataString((description ?? string.Empty).Trim()));
            }

            return string.Join("|", result.ToArray());
        }
    }
}
