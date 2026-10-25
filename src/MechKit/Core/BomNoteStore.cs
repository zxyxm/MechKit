using System;
using System.IO;
using System.Text;
using System.Xml.Linq;
using MechKit.Features;

namespace MechKit.Core
{
    // File-level asset, shared by all occurrences/configurations of the part.
    internal static class BomNoteStore
    {
        private static readonly object Gate = new object();
        internal static event Action<string, string, string> Changed;
        internal static string NotePath(string model) { return BomAssets.AssetPath(model, "", "备注信息", ".xml"); }
        internal static bool IsAutomatic(string model, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            // Channel placeholders must remain editable so a real URL can be supplied.
            if (key == "purchaselink" && (value == "代理" || value == "淘宝" || value == "淘宝加工")) return false;
            if (string.IsNullOrEmpty(model)) return false;
            lock (Gate)
            {
                var field = Read(NotePath(model)).Element(key);
                return field != null && field.Element("manual") == null && !string.IsNullOrWhiteSpace((string)field.Element("generated"));
            }
        }
        private static XElement Read(string path)
        {
            if (!File.Exists(path)) return new XElement("bomNotes", new XAttribute("version", 1));
            var xml = XElement.Load(path);
            if (xml.Name != "bomNotes") throw new InvalidDataException("备注资产格式无效。");
            return xml;
        }
        private static void Write(string path, XElement xml)
        {
            var text = xml.ToString();
            if (!File.Exists(path) || File.ReadAllText(path) != text) BomAssets.Write(path, Encoding.UTF8.GetBytes(text));
        }
        internal static void Save(string model, string key, string value)
        {
            if (key != "remark" && key != "assemblynote" && key != "purchaselink" && key != "unfinished")
                throw new ArgumentException("不支持的备注字段。");
            if (string.IsNullOrWhiteSpace(model) || !File.Exists(model)) throw new InvalidOperationException("请先将零部件保存为外部文件。");
            if (key == "purchaselink") value = PurchaseLinkService.Validate(value);
            lock (Gate)
            {
                var path = NotePath(model); var xml = Read(path);
                var field = xml.Element(key);
                if (field == null) { field = new XElement(key); xml.Add(field); }
                field.SetElementValue("manual", value ?? "");
                Write(path, xml);
            }
            var handler = Changed;
            if (handler != null)
                foreach (Action<string, string, string> callback in handler.GetInvocationList())
                    try { callback(model, key, value ?? ""); } catch (Exception ex) { Log.Warn("同步备注显示失败：" + ex.Message); }
        }
        private static string Resolve(XElement xml, string key, string generated)
        {
            var field = xml.Element(key);
            if (field == null) { field = new XElement(key); xml.Add(field); }
            field.SetElementValue("generated", generated ?? "");
            var manual = field.Element("manual");
            return manual == null ? generated ?? "" : manual.Value;
        }
        internal static void Apply(PartListRow row)
        {
            if (row == null || string.IsNullOrEmpty(row.FilePath) || !File.Exists(row.FilePath)) return;
            lock (Gate)
            {
                var path = NotePath(row.FilePath); var xml = Read(path);
                row.Remark = Resolve(xml, "remark", row.Remark);
                row.AssemblyNote = Resolve(xml, "assemblynote", row.AssemblyNote);
                row.PurchaseLink = Resolve(xml, "purchaselink", row.PurchaseLink);
                row.Unfinished = Resolve(xml, "unfinished", row.Unfinished);
                Write(path, xml);
            }
        }
    }
}
