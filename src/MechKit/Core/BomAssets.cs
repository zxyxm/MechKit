using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;

namespace MechKit.Core
{
    internal static class BomAssets
    {
        internal static string Hash(byte[] bytes)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
        }

        internal static string Stamp(string source)
        {
            // SOLIDWORKS keeps models open for writing. Our read handle must allow
            // that existing access, otherwise previews and export stamps fail.
            var before = new FileInfo(source);
            var length = before.Length;
            var modified = before.LastWriteTimeUtc;
            string stamp;
            using (var stream = new FileStream(source, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (var hash = SHA256.Create())
                stamp = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
            var after = new FileInfo(source);
            if (after.Length != length || after.LastWriteTimeUtc != modified)
                throw new IOException("文件正在保存，暂不复用缓存：" + source);
            return stamp;
        }

        internal static string AssetPath(string source, string configuration, string kind, string extension)
        {
            if (string.IsNullOrWhiteSpace(source)) throw new InvalidOperationException("请先保存零件。");
            var full = Path.GetFullPath(source);
            var name = Path.GetFileNameWithoutExtension(full) + "-" + (string.IsNullOrEmpty(configuration) ? "默认" : configuration);
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (name.Length > 90) name = name.Substring(0, 90);
            var id = Hash(Encoding.UTF8.GetBytes(Path.GetFileName(full).ToUpperInvariant() + "\0" + configuration)).Substring(0, 12);
            return Path.Combine(Path.GetDirectoryName(full), "assets", name + "-" + kind + "-" + id + extension);
        }

        internal static void Write(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        internal static bool IsCurrent(string path, string stamp)
        {
            try
            {
                if (stamp == null || !File.Exists(path) || !File.Exists(path + ".version")) return false;
                return File.ReadAllText(path + ".version") == stamp + "\n" + Stamp(path);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        internal static void MarkCurrent(string path, string stamp)
        {
            Write(path + ".version", Encoding.UTF8.GetBytes(stamp + "\n" + Stamp(path)));
        }

        internal static byte[] Png(Image image)
        {
            using (var bitmap = new Bitmap(image))
            using (var output = new MemoryStream())
            { bitmap.Save(output, ImageFormat.Png); return output.ToArray(); }
        }

        internal static string Illustration(string source, string configuration)
        {
            if (string.IsNullOrEmpty(source)) return null;
            var path = AssetPath(source, configuration, "尺寸说明图", ".png");
            return File.Exists(path) ? path : null;
        }

        internal static string Manual(string model)
        {
            if (string.IsNullOrEmpty(model)) return null;
            var index = AssetPath(model, "", "说明书", ".xml");
            if (!File.Exists(index)) return null;
            var name = (string)System.Xml.Linq.XElement.Load(index).Attribute("file");
            if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name) return null;
            var path = Path.Combine(Path.GetDirectoryName(index), name);
            return File.Exists(path) ? path : null;
        }

        internal static void SaveManual(string model, string source)
        {
            if (!File.Exists(model)) throw new InvalidOperationException("请先保存标准件模型。");
            var extension = Path.GetExtension(source).ToLowerInvariant();
            if (Array.IndexOf(new[] { ".pdf", ".doc", ".docx", ".txt", ".png", ".jpg", ".jpeg", ".bmp" }, extension) < 0)
                throw new InvalidOperationException("支持 PDF、Word、文本及图片说明书。");
            var bytes = File.ReadAllBytes(source);
            var index = AssetPath(model, "", "说明书", ".xml");
            // Keep the manifest reference local to assets; preserve the original filename.
            var local = AssetPath(model, "", "说明书-" + Hash(bytes).Substring(0, 16), extension);
            Write(local, bytes);
            var xml = new System.Xml.Linq.XElement("manual", new System.Xml.Linq.XAttribute("file", Path.GetFileName(local)),
                new System.Xml.Linq.XAttribute("name", Path.GetFileName(source)));
            Write(index, Encoding.UTF8.GetBytes(xml.ToString()));
        }

        internal static string ManualName(string model)
        {
            if (Manual(model) == null) return null;
            return (string)System.Xml.Linq.XElement.Load(AssetPath(model, "", "说明书", ".xml")).Attribute("name");
        }
    }
}
