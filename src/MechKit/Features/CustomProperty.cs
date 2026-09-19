namespace MechKit.Features
{
    /// <summary>一条自定义属性。</summary>
    internal sealed class CustomProperty
    {
        public CustomProperty()
        {
            Type = PropertyTypes.Text;
        }

        public CustomProperty(string name, int type, string value)
        {
            Name = name;
            Type = type;
            Value = value;
        }

        public string Name { get; set; }

        /// <summary>swCustomInfoType_e 取值。</summary>
        public int Type { get; set; }

        /// <summary>属性原始值（可能是 "SW-材料@@@..." 这类表达式）。</summary>
        public string Value { get; set; }

        /// <summary>求值后的显示值。</summary>
        public string ResolvedValue { get; set; }
    }

    /// <summary>属性类型与 swCustomInfoType_e 的映射。</summary>
    internal static class PropertyTypes
    {
        public const int Number = 3;
        public const int Double = 5;
        public const int YesOrNo = 11;
        public const int Text = 30;
        public const int Date = 64;

        public static readonly int[] All = { Text, Number, Double, YesOrNo, Date };

        public static string ToDisplayName(int type)
        {
            switch (type)
            {
                case Number:
                    return "整数";
                case Double:
                    return "小数";
                case YesOrNo:
                    return "是/否";
                case Date:
                    return "日期";
                case Text:
                    return "文本";
                default:
                    return "文本";
            }
        }

        public static int FromDisplayName(string name)
        {
            switch (name)
            {
                case "整数":
                    return Number;
                case "小数":
                    return Double;
                case "是/否":
                    return YesOrNo;
                case "日期":
                    return Date;
                default:
                    return Text;
            }
        }

        /// <summary>规范化用户输入，尽量保证写入值与类型匹配。</summary>
        public static string NormalizeValue(int type, string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            switch (type)
            {
                case Number:
                {
                    long parsed;
                    return long.TryParse(value.Trim(), out parsed)
                        ? parsed.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : value.Trim();
                }
                case Double:
                {
                    double parsed;
                    return double.TryParse(value.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out parsed)
                        ? parsed.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : value.Trim();
                }
                case YesOrNo:
                {
                    var trimmed = value.Trim();
                    if (string.Equals(trimmed, "是", System.StringComparison.Ordinal) ||
                        string.Equals(trimmed, "true", System.StringComparison.OrdinalIgnoreCase) ||
                        trimmed == "1")
                    {
                        return "是";
                    }

                    if (string.Equals(trimmed, "否", System.StringComparison.Ordinal) ||
                        string.Equals(trimmed, "false", System.StringComparison.OrdinalIgnoreCase) ||
                        trimmed == "0")
                    {
                        return "否";
                    }

                    return trimmed;
                }
                default:
                    return value.Trim();
            }
        }
    }
}
