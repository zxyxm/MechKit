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
            naming.UseNameSegments = settings.UseNameSegments;
            naming.SegmentSeparator = string.IsNullOrEmpty(settings.SegmentSeparator)
                ? "_"
                : settings.SegmentSeparator;
            naming.NameSegment = -1;
            naming.MaterialSegment = settings.MaterialSegment;
            naming.MachinedSegments = ParseMachinedSegments(settings.MachinedSegments);
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
            var result = new List<string>();
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
                        default: result.Add("自定义"); break;
                    }
                }
            }

            return string.Join(" → ", result.ToArray());
        }

        /// <summary>解析前缀列表（逗号 / 分号 / 顿号分隔）。</summary>
        public static string[] ParsePrefixes(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return result.ToArray();
            }

            foreach (var part in text.Split(new[] { ',', '，', ';', '；', '、' }))
            {
                var value = part.Trim().TrimEnd('_', '*', '＊');
                if (value.Length > 0 && !result.Contains(value))
                {
                    result.Add(value);
                }
            }

            return result.ToArray();
        }
    }
}
