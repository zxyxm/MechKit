using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MechKit.Core;

namespace MechKit.UI
{
    /// <summary>大类前缀配置，设置页与命名规则页共用。</summary>
    internal sealed class CategoryRulesPanel : UserControl
    {
        private readonly IAddinHost _host;
        private readonly Dictionary<int, TextBox> _editors = new Dictionary<int, TextBox>();
        private readonly List<TextBox> _externalEditors = new List<TextBox>();
        private TableLayoutPanel _externalRows;
        private Button _save;
        private static readonly string[] Names = { "库存件", "备件", "外部图纸" };
        private static readonly string[] Defaults = { "库存", "备件", "外部图纸" };

        public CategoryRulesPanel(IAddinHost host, int category = -1)
        {
            _host = host;
            Dock = DockStyle.Fill;
            BackColor = Theme.Surface;
            AutoScroll = true;
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2,
                Padding = new Padding(16), BackColor = Theme.Surface
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var intro = Theme.CreateLabel("大类：加工件、标准件、参考件、库存件、备件、外部图纸", Theme.BodyBold, Theme.Text);
            intro.Dock = DockStyle.Fill;
            intro.Height = 38;
            layout.Controls.Add(intro, 0, 0);
            layout.SetColumnSpan(intro, 2);
            int row = 1;
            for (int index = 0; index < Names.Length; index++)
            {
                if (category >= 0 && index != category) continue;
                if (index == 2)
                {
                    _externalRows = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, BackColor = Theme.Surface };
                    _externalRows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
                    _externalRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                    _externalRows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
                    layout.Controls.Add(_externalRows, 0, row++);
                    layout.SetColumnSpan(_externalRows, 2);
                    var externalHint = Theme.CreateLabel("每条填写一个名称前缀，例如外部图纸1：BRC-，只识别 BRC-支架，不识别 BRC001。可添加多条；整体计入 BOM，装配体不展开。", Theme.Small, Theme.Muted);
                    externalHint.Dock = DockStyle.Fill;
                    externalHint.Height = 48;
                    layout.Controls.Add(externalHint, 0, row++);
                    layout.SetColumnSpan(externalHint, 2);
                    continue;
                }
                var label = Theme.CreateFieldLabel(Names[index]);
                label.Margin = new Padding(0, 8, 8, 0);
                layout.Controls.Add(label, 0, row);
                var editor = Theme.CreateTextBox();
                editor.Dock = DockStyle.Fill;
                editor.Margin = new Padding(0, 4, 0, 8);
                layout.Controls.Add(editor, 1, row++);
                _editors[index] = editor;
                var hint = Theme.CreateLabel(index == 2
                    ? "按零件名开头匹配，例如填写 BRC，可识别 BRC-支架、BRC001；整体计入 BOM，装配体不展开。"
                    : "按名称开头匹配；整体计入 BOM，装配体不展开。默认示例：" + Defaults[index] + "-安装板。",
                    Theme.Small, Theme.Muted);
                hint.Dock = DockStyle.Fill;
                hint.Height = 42;
                layout.Controls.Add(hint, 1, row++);
            }
            var note = Theme.CreateLabel("多个前缀用空格、逗号或分号分隔。参考件优先排除；指定外部图纸前缀优先于库存、备件和普通命名规则。未匹配名称标为命名不规范，整行红底提醒。",
                Theme.Small, Theme.Muted);
            note.Dock = DockStyle.Fill;
            note.Height = 64;
            layout.Controls.Add(note, 0, row++);
            layout.SetColumnSpan(note, 2);
            var save = Theme.CreatePrimaryButton("保存大类规则");
            _save = save;
            save.Width = 148;
            save.Click += delegate
            {
                SaveToSettings();
                _host.Settings.Save();
                _host.RefreshNamingCommands();
                save.Text = "已保存";
            };
            foreach (var editor in _editors.Values)
                editor.TextChanged += delegate { save.Text = "保存大类规则"; };
            layout.Controls.Add(save, 1, row);
            Controls.Add(layout);
            LoadFromSettings();
        }

        public void LoadFromSettings()
        {
            var settings = _host.Settings;
            foreach (var editor in _editors)
                editor.Value.Text = editor.Key == 0 ? settings.StockPrefixes :
                    editor.Key == 1 ? settings.SparePrefixes : settings.ExternalDrawingPrefixes;
            if (_externalRows != null) RebuildExternalRows(NamingOptionsFactory.ParsePrefixes(settings.ExternalDrawingPrefixes, true));
        }

        public void ResetDefaults()
        {
            foreach (var editor in _editors) editor.Value.Text = Defaults[editor.Key];
            if (_externalRows != null) RebuildExternalRows(new[] { Defaults[2] });
        }

        public void SaveToSettings()
        {
            foreach (var editor in _editors)
            {
                if (editor.Key == 2) continue;
                var value = NamingOptionsFactory.SerializePrefixes(NamingOptionsFactory.ParsePrefixes(editor.Value.Text));
                if (editor.Key == 0) _host.Settings.StockPrefixes = value;
                else if (editor.Key == 1) _host.Settings.SparePrefixes = value;
                else _host.Settings.ExternalDrawingPrefixes = value;
            }
            if (_externalRows != null)
            {
                var prefixes = new List<string>();
                foreach (var editor in _externalEditors) prefixes.AddRange(NamingOptionsFactory.ParsePrefixes(editor.Text, true));
                _host.Settings.ExternalDrawingPrefixes = NamingOptionsFactory.SerializePrefixes(prefixes.ToArray(), true);
            }
        }

        private void RebuildExternalRows(IEnumerable<string> prefixes)
        {
            var values = new List<string>(prefixes);
            if (values.Count == 0) values.Add(string.Empty);
            _externalRows.SuspendLayout();
            var oldControls = new List<Control>();
            foreach (Control control in _externalRows.Controls) oldControls.Add(control);
            _externalRows.Controls.Clear();
            foreach (var control in oldControls) control.Dispose();
            _externalRows.RowStyles.Clear();
            _externalRows.RowCount = values.Count + 1;
            _externalEditors.Clear();
            for (var i = 0; i < values.Count; i++)
            {
                var index = i;
                var label = Theme.CreateFieldLabel("外部图纸" + (i + 1));
                label.Margin = new Padding(0, 8, 8, 0);
                var editor = Theme.CreateTextBox();
                editor.Dock = DockStyle.Fill;
                editor.Margin = new Padding(0, 4, 8, 8);
                editor.Text = values[i];
                editor.TextChanged += delegate { _save.Text = "保存大类规则"; };
                _externalEditors.Add(editor);
                var remove = Theme.CreateSecondaryButton("删除");
                remove.Width = 64;
                remove.Click += delegate
                {
                    var current = ExternalRowValues();
                    current.RemoveAt(index);
                    RebuildExternalRows(current);
                    _save.Text = "保存大类规则";
                };
                _externalRows.Controls.Add(label, 0, i);
                _externalRows.Controls.Add(editor, 1, i);
                _externalRows.Controls.Add(remove, 2, i);
            }
            _editors[2] = _externalEditors[0];
            var add = Theme.CreateSecondaryButton("添加外部图纸前缀");
            add.Width = 176;
            add.Click += delegate
            {
                var current = ExternalRowValues();
                current.Add(string.Empty);
                RebuildExternalRows(current);
                _save.Text = "保存大类规则";
                _externalEditors[_externalEditors.Count - 1].Focus();
            };
            _externalRows.Controls.Add(add, 1, values.Count);
            _externalRows.ResumeLayout();
        }

        private List<string> ExternalRowValues()
        {
            var values = new List<string>();
            foreach (var editor in _externalEditors) values.Add(editor.Text);
            return values;
        }
    }
}
