using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using MechKit.Core;
using SolidWorks.Interop.sldworks;

namespace MechKit.Features
{
    internal sealed class RequirementLibrary
    {
        internal readonly Dictionary<string, List<string>> Groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        internal static string DefaultPath { get { return Path.Combine(AppPaths.Root, "technical-requirements.xml"); } }
        internal static RequirementLibrary Load(string path)
        {
            var library = new RequirementLibrary();
            if (!File.Exists(path))
            {
                library.Groups["公共"] = new List<string> { "去油污、去毛刺，锐边倒钝。", "未注尺寸按随图附件三维模型尺寸。", "材料、数量、表面处理参见 BOM。" };
                return library;
            }
            var root = XElement.Load(path);
            if (root.Name != "requirements") throw new InvalidDataException("技术要求库格式不正确。");
            foreach (var group in root.Elements("group"))
            {
                var name = (string)group.Attribute("name");
                if (!string.IsNullOrWhiteSpace(name)) library.Groups[name] = Normalize(group.Elements("item").Select(e => e.Value));
            }
            return library;
        }
        internal static List<string> Normalize(IEnumerable<string> lines)
        {
            return lines.Select(s => (s ?? "").Trim()).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        }
        internal List<string> Get(string key) { List<string> lines; return Groups.TryGetValue(key, out lines) ? new List<string>(lines) : new List<string>(); }
        internal List<string> Combined(string material) { return Normalize(Get("公共").Concat(Get("材料:" + material.Trim()))); }
        internal void Save(string path)
        {
            var root = new XElement("requirements", new XAttribute("version", 1),
                Groups.OrderBy(g => g.Key).Select(g => new XElement("group", new XAttribute("name", g.Key), Normalize(g.Value).Select(s => new XElement("item", s)))));
            BomAssets.Write(path, Encoding.UTF8.GetBytes(root.ToString()));
        }
        internal static string Compose(IEnumerable<string> lines)
        {
            var clean = Normalize(lines);
            return clean.Count == 0 ? "" : "技术要求：\r\n" + string.Join("\r\n", clean.Select((s, i) => (i + 1) + ". " + s));
        }
    }

    internal static class TechnicalRequirements
    {
        internal static string DetectMaterial(ISldWorks app, NamingOptions naming)
        {
            var doc = SwUtils.ActiveDoc(app);
            if (doc == null) return "";
            var path = doc.GetPathName();
            if (doc.GetType() == SwUtils.DocDrawing)
            {
                var sheetView = ((DrawingDoc)doc).GetFirstView() as View;
                var view = sheetView == null ? null : sheetView.GetNextView() as View;
                if (view != null && !string.IsNullOrEmpty(view.GetReferencedModelName())) path = view.GetReferencedModelName();
            }
            string material, process, surface;
            if (naming.TryResolveMachinedMaterialProcessSurface(path, out material, out process, out surface)) return material;
            return naming.IsMachinedName(Path.GetFileNameWithoutExtension(path))
                ? naming.ResolveMachinedSegment(path, MachinedSegmentKind.Material) : "";
        }

        internal static void Insert(ISldWorks app, string text, bool replaceSelected, double xMm, double yMm)
        {
            var doc = SwUtils.ActiveDoc(app);
            if (doc == null || doc.GetType() != SwUtils.DocDrawing) throw new InvalidOperationException("请先打开工程图。");
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("请至少勾选一条技术要求。");
            if (replaceSelected)
            {
                var selection = (SelectionMgr)doc.SelectionManager;
                if (selection.GetSelectedObjectCount2(-1) != 1) throw new InvalidOperationException("请在工程图中选中一条要替换的注释。");
                var selected = selection.GetSelectedObject6(1, -1);
                var note = selected as Note;
                var annotation = selected as Annotation;
                if (note == null && annotation != null) note = annotation.GetSpecificAnnotation() as Note;
                if (note == null) throw new InvalidOperationException("选中的不是文字注释，请重新选择。");
                if (!note.SetText(text)) throw new InvalidOperationException("替换注释失败。");
            }
            else
            {
                var drawing = (DrawingDoc)doc;
                var sheet = (Sheet)drawing.GetCurrentSheet();
                var properties = (double[])sheet.GetProperties2();
                var x = xMm / 1000.0; var y = yMm / 1000.0;
                if (x <= 0 || y <= 0 || x >= properties[5] || y >= properties[6]) throw new InvalidOperationException("插入位置必须在当前图纸范围内。");
                doc.ClearSelection2(true);
                if (!drawing.ActivateView("")) throw new InvalidOperationException("无法激活图纸，请退出草图或图纸格式编辑后重试。");
                var note = doc.InsertNote(text) as Note;
                if (note == null) throw new InvalidOperationException("无法插入技术要求注释。");
                var annotation = (Annotation)note.GetAnnotation();
                if (!annotation.SetPosition2(x, y, 0)) throw new InvalidOperationException("注释已创建，但定位失败，请手动拖动注释。");
            }
            doc.GraphicsRedraw2();
        }
    }
}
