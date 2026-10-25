using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MechKit.Core;

namespace MechKit.UI
{
    internal sealed class DesignNotePanel : UserControl
    {
        private readonly CheckBox _completed = new CheckBox { Text = "设计备注 · 已完成", AutoSize = true };
        private readonly Label _document = new Label { AutoEllipsis = true, Dock = DockStyle.Fill };
        private readonly TextBox _text = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        private readonly Label _times = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
        private readonly Dictionary<string, string> _drafts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string _path, _savedText = "";
        private bool _binding, _saving;

        internal DesignNotePanel()
        {
            Dock = DockStyle.Top; Height = 210; Padding = new Padding(10, 6, 10, 6);
            BackColor = Theme.Surface; Font = Theme.Body;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            _completed.Dock = DockStyle.Fill;
            var save = Theme.CreateSecondaryButton("保存"); save.Dock = DockStyle.Fill; save.Margin = Padding.Empty;
            header.Controls.Add(_completed, 0, 0); header.Controls.Add(save, 1, 0);
            layout.Controls.Add(header, 0, 0); layout.Controls.Add(_document, 0, 1);
            layout.Controls.Add(_text, 0, 2); layout.Controls.Add(_times, 0, 3);
            _times.Font = Theme.Small; _times.ForeColor = Theme.Muted;
            Controls.Add(layout);
            save.Click += delegate { Save(); };
            _text.Leave += delegate { if (!_binding && _text.Text.Trim() != _savedText) Save(); };
            _completed.CheckedChanged += delegate { if (!_binding) Save(); };
            DesignNoteStore.Changed += OnNoteChanged;
        }

        internal void Bind(string path, string title)
        {
            var samePath = string.Equals(_path, path, StringComparison.OrdinalIgnoreCase);
            if (!samePath && !string.IsNullOrEmpty(_path) && _text.Text.Trim() != _savedText)
                Save();
            _path = path;
            _document.Text = title ?? "（未打开零部件）";
            Enabled = !string.IsNullOrEmpty(path) && File.Exists(path);
            if (samePath && Enabled && _text.Focused && _text.Text.Trim() != _savedText) return;
            Reload();
        }

        private void Reload()
        {
            _binding = true;
            try
            {
                var note = DesignNoteStore.Load(_path);
                _savedText = note.Text;
                string draft;
                _text.Text = _path != null && _drafts.TryGetValue(_path, out draft) ? draft : note.Text;
                _completed.Checked = note.Completed;
                _completed.Enabled = note.Text.Length > 0;
                _times.Text = "创建：" + (note.CreatedAt.Length == 0 ? "—" : note.CreatedAt) +
                    "\r\n完成：" + (note.Completed ? note.CompletedAt : "未完成");
            }
            catch (Exception ex) { _text.Clear(); _completed.Checked = false; _times.Text = "读取失败：" + ex.Message; }
            finally { _binding = false; }
        }

        private void Save()
        {
            if (_binding || _saving || string.IsNullOrEmpty(_path)) return;
            _saving = true;
            try
            {
                DesignNoteStore.Save(_path, _text.Text, _completed.Checked);
                _drafts.Remove(_path);
                Reload();
            }
            catch (Exception ex) { _drafts[_path] = _text.Text; _times.Text = "未保存：" + ex.Message; }
            finally { _saving = false; }
        }

        private void OnNoteChanged(string path)
        {
            if (!_saving && string.Equals(path, _path, StringComparison.OrdinalIgnoreCase) && _text.Text.Trim() == _savedText) Reload();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Save(); DesignNoteStore.Changed -= OnNoteChanged; }
            base.Dispose(disposing);
        }
    }
}
