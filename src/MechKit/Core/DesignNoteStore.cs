using System;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace MechKit.Core
{
    internal sealed class DesignNote
    {
        public string Text { get; set; } = "";
        public string CreatedAt { get; set; } = "";
        public string CompletedAt { get; set; } = "";
        public bool Completed { get { return CompletedAt.Length > 0; } }
    }

    // File-level notes are shared by all configurations and stored with the project assets.
    internal static class DesignNoteStore
    {
        internal static event Action<string> Changed;
        internal static string NotePath(string model)
        { return BomAssets.AssetPath(model, "", "设计备注", ".xml"); }

        internal static DesignNote Load(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return new DesignNote();
            var path = NotePath(model);
            if (!File.Exists(path)) return new DesignNote();
            var xml = XElement.Load(path);
            if (xml.Name != "designNote") throw new InvalidDataException("设计备注文件格式无效。");
            return new DesignNote { Text = (string)xml.Element("text") ?? "",
                CreatedAt = (string)xml.Element("createdAt") ?? "",
                CompletedAt = (string)xml.Element("completedAt") ?? "" };
        }

        internal static DesignNote Save(string model, string text, bool completed)
        {
            if (string.IsNullOrWhiteSpace(model) || !File.Exists(model))
                throw new InvalidOperationException("请先将零部件保存为外部文件。");
            var note = Load(model);
            text = (text ?? "").Trim();
            var changed = note.Text != text;
            var now = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
            if (text.Length == 0) note = new DesignNote();
            else
            {
                if (note.CreatedAt.Length == 0) note.CreatedAt = now;
                // Editing a completed requirement reopens it for review.
                if (changed) completed = false;
                note.CompletedAt = completed ? (note.Completed ? note.CompletedAt : now) : "";
                note.Text = text;
            }
            var xml = new XElement("designNote", new XAttribute("version", 1),
                new XElement("text", note.Text), new XElement("createdAt", note.CreatedAt),
                new XElement("completedAt", note.CompletedAt));
            BomAssets.Write(NotePath(model), Encoding.UTF8.GetBytes(xml.ToString()));
            var handler = Changed;
            if (handler != null)
                foreach (Action<string> callback in handler.GetInvocationList())
                    try { callback(model); } catch (Exception ex) { Log.Warn("刷新设计备注失败：" + ex.Message); }
            return note;
        }
    }
}
