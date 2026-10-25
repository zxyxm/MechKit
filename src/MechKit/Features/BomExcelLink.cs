using System;
using System.IO;
using System.Text;
using System.Xml.Linq;
using MechKit.Core;

namespace MechKit.Features
{
    internal static class BomExcelLink
    {
        internal static string Snapshot(PartListRow row, int index)
        {
            var xml = new XElement("bomLink", new XAttribute("version", 1),
                new XElement("filepath", row.FilePath ?? ""), new XElement("configuration", row.Configuration ?? ""));
            foreach (var key in PartListService.ParseColumnOrder(null))
                xml.Add(new XElement(key, PartListService.ColumnValue(row, index, key) ?? ""));
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(xml.ToString()));
        }
        internal static XElement ReadSnapshot(string text)
        {
            if (string.IsNullOrEmpty(text)) throw new InvalidDataException("缺少关联快照，请使用新版重新导出的 Excel。");
            var xml = XElement.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(text)));
            if (xml.Name != "bomLink" || (string)xml.Attribute("version") != "1") throw new InvalidDataException("关联快照格式不支持。");
            return xml;
        }
        private static string LinkPath(string model)
        {
            if (string.IsNullOrEmpty(model)) throw new InvalidOperationException("请先保存 BOM 来源模型。");
            var hash = BomAssets.Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(model).ToUpperInvariant()));
            return Path.Combine(AppPaths.Root, "excel-links", hash + ".txt");
        }
        internal static void Save(string model, string workbook) { BomAssets.Write(LinkPath(model), Encoding.UTF8.GetBytes(Path.GetFullPath(workbook))); }
        internal static string Load(string model) { var path = LinkPath(model); return File.Exists(path) ? File.ReadAllText(path) : null; }
        internal static bool Editable(string key) { return key == "remark" || key == "assemblynote" || key == "purchaselink" || key == "unfinished"; }
    }
}
