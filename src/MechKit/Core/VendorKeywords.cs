namespace MechKit.Core
{
    /// <summary>
    /// 外购件 / 标准件的常见关键词。
    /// 加工件数量统计必须先把这些排除掉，否则导轨、气缸、传感器会被算成加工件。
    /// </summary>
    internal static class VendorKeywords
    {
        /// <summary>外购件厂商（文件名里出现即视为外购件）。</summary>
        public static readonly string[] Purchased =
        {
            "嘉立创", "jlc", "米思米", "misumi", "怡合达", "yiheda", "yhda",
            "thk", "hiwin", "上银", "银泰", "pmi", "上银",
            "smc", "亚德客", "airtac", "festo", "费斯托", "ckd", "koganei",
            "omron", "欧姆龙", "keyence", "基恩士", "panasonic", "松下", "balluff",
            "siemens", "西门子", "schneider", "施耐德", "mitsubishi", "三菱",
            "sew", "东方马达", "orion", "delixi", "台达", "delta",
            "igubal", "igus", "易格斯", "hgw", "egt", "skf", "nsk", "fag"
        };

        /// <summary>标准件标识（文件名里出现即视为标准件）。</summary>
        public static readonly string[] Standard =
        {
            "gb/t", "gbt", "gb-", "din", "iso-", "jis", "ansi",
            "螺栓", "螺钉", "螺母", "垫圈", "销钉", "轴承", "卡簧", "挡圈"
        };

        /// <summary>返回关键词匹配结果："标准件" / "外购件" / 空字符串。</summary>
        public static string Match(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return string.Empty;
            }

            var text = fileName.ToLowerInvariant();

            if (Contains(text, Standard))
            {
                return "标准件";
            }

            if (Contains(text, Purchased))
            {
                return "外购件";
            }

            return string.Empty;
        }

        private static bool Contains(string text, string[] keywords)
        {
            foreach (var keyword in keywords)
            {
                if (!string.IsNullOrEmpty(keyword) && text.Contains(keyword))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
