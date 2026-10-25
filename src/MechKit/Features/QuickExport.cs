using System;
using System.IO;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using MechKit.Core;

namespace MechKit.Features
{
    internal static class QuickExport
    {
        internal static string FieldFilter = "";
        internal static string CategoryFilter = "全部";
        internal static bool OnlyWithDrawing = false;
        internal static bool Overwrite = true;

        internal static string ProcessField(string path, NamingOptions naming)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            string material, process, surface;
            return naming.TryResolveMachinedMaterialProcessSurface(path, out material, out process, out surface)
                ? (process ?? "").Trim() : "";
        }

        internal static bool MatchesFilter(string path, string filter, NamingOptions naming)
        {
            if (string.IsNullOrWhiteSpace(filter) || filter == "全部") return true;
            foreach (var field in filter.Split(',', '，', ';', '；'))
                if (string.Equals(ProcessField(path, naming), field.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static string CategoryField(string path, NamingOptions naming)
        {
            var custom = naming.MatchCategory(path);
            if (custom != null) return custom.Name;
            switch (naming.RecognizeDesignNode(path))
            {
                case DesignNodeKind.Machined: return "加工件";
                case DesignNodeKind.Standard: return "标准件";
                default: return "未分类";
            }
        }

        internal static string SecondaryField(string path, NamingOptions naming)
        {
            var category = CategoryField(path, naming);
            if (category == "加工件") return ProcessField(path, naming);
            if (category == "标准件")
            {
                var fields = Path.GetFileNameWithoutExtension(path).Split('-', '_');
                return fields.Length > 1 ? fields[1].Trim() : "";
            }
            return naming.MatchCategory(path)?.Match(path) ?? "";
        }

        internal static bool MatchesSelection(string path, NamingOptions naming)
        {
            if (string.IsNullOrWhiteSpace(CategoryFilter) || CategoryFilter == "全部") return true;
            if (!string.Equals(CategoryField(path, naming), CategoryFilter, StringComparison.OrdinalIgnoreCase)) return false;
            if (string.IsNullOrWhiteSpace(FieldFilter) || FieldFilter == "全部") return true;
            var value = SecondaryField(path, naming);
            foreach (var field in FieldFilter.Split(',', '，', ';', '；'))
                if (string.Equals(value, field.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static List<string> CollectFiles(ModelDoc2 doc, Action<string> log)
        {
            var paths = CollectAllParts(doc, log);
            var naming = NamingOptionsFactory.FromSettings(AddinSettings.Load());
            paths.RemoveAll(path => !MatchesSelection(path, naming));
            return OnlyWithDrawing ? CollectPairedParts(paths, log) : paths;
        }

        internal static List<string> CollectAllParts(ModelDoc2 doc, Action<string> log)
        {
            var paths = new List<string>();
            if (doc.GetType() != SwUtils.DocAssembly)
            {
                paths.Add(doc.GetPathName());
                return paths;
            }

            // Resolve lightweight subassemblies so GetChildren can traverse them.
            ((AssemblyDoc)doc).ResolveAllLightWeightComponents(false);
            // Always use the active assembly; selection must never narrow an export.
            var components = ((AssemblyDoc)doc).GetComponents(true) as object[];
            if (components != null)
                foreach (var value in components)
                    CollectComponent(value as Component2, paths, log);
            var result = new List<string>(new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase));
            result.RemoveAll(path =>
            {
                if (File.Exists(path)) return false;
                log("跳过尚未保存为外部文件的零件：" + path);
                return true;
            });
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private static void CollectComponent(Component2 component, List<string> paths, Action<string> log)
        {
            if (component == null) return;
            if (component.IsSuppressed())
            {
                log("跳过压缩组件：" + component.Name2);
                return;
            }
            var path = component.GetPathName();
            if (SwUtils.DocTypeFromPath(path) == SwUtils.DocPart) paths.Add(path);
            var children = component.GetChildren() as object[];
            if (children != null)
                foreach (var child in children) CollectComponent(child as Component2, paths, log);
        }

        internal static List<string> CollectPairedParts(IEnumerable<string> paths, Action<string> log)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                if (SwUtils.DocTypeFromPath(path) != SwUtils.DocPart) continue;
                var fullPath = Path.GetFullPath(path);
                if (!seen.Add(fullPath)) continue;
                if (!File.Exists(fullPath) || !File.Exists(Path.ChangeExtension(fullPath, ".slddrw")))
                {
                    log("跳过未保存或无同名工程图的零件：" + fullPath);
                    continue;
                }
                result.Add(fullPath);
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private static string SettingsPath { get { return Path.Combine(AppPaths.Root, "quick-export-folder.txt"); } }
        internal static string DefaultFolder(DateTime date)
        {
            return Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory), date.ToString("yyyyMMdd") + "加工待做");
        }
        internal static string Folder
        {
            get
            {
                if (File.Exists(SettingsPath))
                {
                    var folder = File.ReadAllText(SettingsPath).Trim();
                    if (!string.IsNullOrEmpty(folder)) return folder;
                }
                return DefaultFolder(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "China Standard Time"));
            }
        }
        internal static void SetFolder(string folder)
        {
            BomAssets.Write(SettingsPath, System.Text.Encoding.UTF8.GetBytes(folder ?? ""));
        }
    }
}
