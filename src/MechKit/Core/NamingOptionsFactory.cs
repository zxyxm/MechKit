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
            naming.BomPrefixes = ParsePrefixes(settings.BomPrefixes);
            naming.RequireBomPattern = settings.BomRequirePattern;
            return naming;
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
