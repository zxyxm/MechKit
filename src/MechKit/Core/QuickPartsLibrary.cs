using System;
using Environment = System.Environment;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace MechKit.Core
{
    internal sealed class QuickPartsLibrary
    {
        public const string Uncategorized = "未分类";
        public static readonly string[] Categories = { "电控件", "气动件", "紧固件", "其他" };
        public string Root { get; private set; }
        public QuickPartsLibrary(string root = null)
        {
            Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MechKit", "零件库");
            foreach (var category in Categories) Directory.CreateDirectory(Path.Combine(Root, category));
        }

        public string[] Search(string category, string query)
        {
            var folder = Categories.Contains(category) ? Path.Combine(Root, category) : Root;
            var words = (query ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return Directory.EnumerateFiles(folder, "*.sldprt", category == Uncategorized ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories)
                .Where(p => !Path.GetFileName(p).StartsWith("~$") && words.All(w =>
                    p.Substring(Root.Length).IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }

        public string GetCategory(string path)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            return string.Equals(directory.TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                ? Uncategorized : Path.GetFileName(directory);
        }

        public static bool Is2024(object history)
        {
            var entries = history as Array;
            if (entries == null || entries.Length == 0) return false;
            // Version history includes earlier saves: the largest release is the actual file version.
            var versions = new List<int>();
            foreach (var entry in entries)
            {
                int version;
                if (!int.TryParse(Convert.ToString(entry).Split(',')[0].Trim(), out version)) return false;
                versions.Add(version);
            }
            return versions.Max() == 17000;
        }

        public static void Validate(ISldWorks sw, string path)
        {
            if (sw == null) throw new InvalidOperationException("请在 SOLIDWORKS 中使用零件库。");
            if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".sldprt", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请选择已保存的 SLDPRT 零件。");
            if (!Is2024(sw.VersionHistory(path)))
                throw new InvalidOperationException("仅接收 SOLIDWORKS 2024 保存的零件，请先用 2024 保存后再添加。");
            var dependencies = sw.GetDocumentDependencies2(path, true, false, false) as Array;
            if (dependencies != null && dependencies.Length != 0)
                throw new InvalidOperationException("该零件引用了其他模型，请先制作无外部模型引用的独立零件，再添加进库。");
            var open = sw.GetOpenDocumentByName(path) as IModelDoc2;
            if (open != null && open.GetSaveFlag())
                throw new InvalidOperationException("零件有未保存的修改，请先保存后再操作。");
        }

        public string Import(ISldWorks sw, string source, string category)
        {
            if (category != Uncategorized && !Categories.Contains(category)) throw new ArgumentException("请选择入库分类。");
            Validate(sw, source);
            return CopyUnique(source, category == Uncategorized ? Root : Path.Combine(Root, category));
        }

        public static string CopyUnique(string source, string directory)
        {
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
            var stem = Path.GetFileNameWithoutExtension(source);
            var ext = Path.GetExtension(source);
            for (int i = 0; i < 10000; i++)
            {
                var target = Path.Combine(directory, stem + (i == 0 ? "" : "_" + i) + ext);
                // CreateNew reserves the name atomically and never overwrites an existing file.
                FileStream output;
                try { output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
                catch (IOException) { if (File.Exists(target)) continue; throw; }
                try
                {
                    using (output)
                    using (var input = File.OpenRead(source)) input.CopyTo(output);
                    return target;
                }
                catch { File.Delete(target); throw; }
            }
            throw new IOException("同名副本过多，请清理或更改零件名称。");
        }
    }
}
