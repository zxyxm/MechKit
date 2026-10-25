using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using MechKit.Core;
using MechKit.Features;

namespace MechKit.UI
{
    internal sealed class BomNotePanel : UserControl
    {
        private readonly DataGridView _fields = new DataGridView();
        private readonly Label _status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
        private readonly string[] _keys = { "purchaselink", "remark", "assemblynote", "unfinished" };
        private readonly string[] _saved = new string[4];
        private readonly Dictionary<string, string[]> _drafts = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        private string _path;
        private bool _binding, _saving;
        private bool _bound;
        private readonly string[] _labels = { "购买链接", "备注", "安装说明", "未尽事宜" };
        private readonly Font _linkFont = new Font(Theme.Body, FontStyle.Underline);
        internal bool Editing { get { return _fields.IsCurrentCellInEditMode; } }
        internal BomNotePanel()
        {
            Dock = DockStyle.Fill; BackColor = Theme.Surface; Padding = new Padding(8);
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            table.Controls.Add(new Label { Text = "零部件手工填写（同步 BOM）", Dock = DockStyle.Fill }, 0, 0);
            _fields.Dock = DockStyle.Fill;
            _fields.AllowUserToAddRows = false; _fields.AllowUserToDeleteRows = false;
            _fields.RowHeadersVisible = false; _fields.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _fields.Columns.Add(new DataGridViewTextBoxColumn { Name = "field", HeaderText = "字段", ReadOnly = true, FillWeight = 28 });
            _fields.Columns.Add(new DataGridViewTextBoxColumn { Name = "value", HeaderText = "内容", FillWeight = 72 });
            foreach (var name in new[] { "购买链接", "备注", "安装说明", "未尽事宜" }) _fields.Rows.Add(name, "");
            _fields.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _fields.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            _fields.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            _fields.CellDoubleClick += BeginFieldEdit;
            _fields.CellEndEdit += delegate { Save(); };
            _fields.CellFormatting += delegate(object sender, DataGridViewCellFormattingEventArgs e)
            {
                if (e.RowIndex == 0 && e.ColumnIndex == 1 && !_fields.Rows[0].Cells[1].ReadOnly && PurchaseLinkService.IsWebLink(Convert.ToString(e.Value)))
                { e.CellStyle.ForeColor = Theme.Accent; e.CellStyle.Font = _linkFont; }
            };
            _fields.Rows[0].Cells[1].ToolTipText = "双击编辑；Ctrl＋左键打开购买链接";
            _fields.CellMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e)
            {
                if (e.RowIndex == 0 && e.ColumnIndex == 1 && e.Button == MouseButtons.Left && (ModifierKeys & Keys.Control) != 0)
                    OpenPurchaseLink(Value(0));
            };
            _fields.EditingControlShowing += delegate(object sender, DataGridViewEditingControlShowingEventArgs e)
            {
                e.Control.MouseClick -= OnEditorLinkClick;
                e.Control.MouseClick += OnEditorLinkClick;
            };
            table.Controls.Add(_fields, 0, 1);
            var save = Theme.CreateSecondaryButton("保存并同步 BOM"); save.Dock = DockStyle.Fill;
            save.Click += delegate { _fields.EndEdit(); Save(); };
            table.Controls.Add(save, 0, 2); table.Controls.Add(_status, 0, 3);
            Controls.Add(table); BomNoteStore.Changed += Changed;
        }
        private string Value(int i) { return Convert.ToString(_fields.Rows[i].Cells[1].Value) ?? ""; }
        private void OnEditorLinkClick(object sender, MouseEventArgs e)
        {
            if (_fields.CurrentCell == null || _fields.CurrentCell.RowIndex != 0 || _fields.CurrentCell.ColumnIndex != 1 ||
                e.Button != MouseButtons.Left || (ModifierKeys & Keys.Control) == 0) return;
            var text = ((Control)sender).Text;
            if (!PurchaseLinkService.IsWebLink(text)) return;
            _fields.EndEdit(); Save(); OpenPurchaseLink(text);
        }
        private void OpenPurchaseLink(string value)
        {
            if (!PurchaseLinkService.IsWebLink(value)) return;
            try { WindowLayout.OpenExternal(new System.Diagnostics.ProcessStartInfo(value.Trim()) { UseShellExecute = true }); }
            catch (Exception ex) { _status.Text = "打开购买链接失败：" + ex.Message; }
        }
        private void BeginFieldEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || !_bound || _fields.Rows[e.RowIndex].Cells[1].ReadOnly) return;
            _fields.CurrentCell = _fields.Rows[e.RowIndex].Cells[1];
            _fields.BeginEdit(true);
        }
        internal void Bind(PartListRow row)
        {
            var path = row == null ? null : row.FilePath;
            if (string.Equals(path, _path, StringComparison.OrdinalIgnoreCase) && Editing) return;
            _fields.EndEdit(); Save();
            _binding = true;
            try
            {
                _path = path; _bound = !string.IsNullOrEmpty(path) && File.Exists(path);
                Enabled = true;
                var values = row == null ? new[] { "", "", "", "" } : new[] { row.PurchaseLink, row.Remark, row.AssemblyNote, row.Unfinished };
                string[] draft = null;
                if (path != null) _drafts.TryGetValue(path, out draft);
                for (int i = 0; i < 4; i++) { _saved[i] = values[i] ?? ""; _fields.Rows[i].Cells[1].Value = draft == null ? _saved[i] : draft[i]; }
                for (int i = 0; i < 4; i++) UpdateFieldState(i);
                _status.Text = _bound ? "绑定：" + Path.GetFileName(path) : string.IsNullOrEmpty(path) ? "请选择已保存的零部件" : "文件不可用：" + path;
            }
            finally { _binding = false; }
        }
        private void Save()
        {
            if (_binding || _saving || !_bound || string.IsNullOrEmpty(_path)) return;
            _saving = true;
            try
            {
                for (int i = 0; i < 4; i++) if (!_fields.Rows[i].Cells[1].ReadOnly && Value(i) != _saved[i])
                {
                    var value = i == 0 ? PurchaseLinkService.Validate(Value(i)) : Value(i);
                    BomNoteStore.Save(_path, _keys[i], value); _saved[i] = value; _fields.Rows[i].Cells[1].Value = value;
                }
                _drafts.Remove(_path); _status.Text = "已保存至资产并同步 BOM";
            }
            catch (Exception ex) { _drafts[_path] = new[] { Value(0), Value(1), Value(2), Value(3) }; _status.Text = "未保存：" + ex.Message; }
            finally { _saving = false; }
        }
        private void Changed(string path, string key, string value)
        {
            if (_saving || !string.Equals(path, _path, StringComparison.OrdinalIgnoreCase)) return;
            int i = Array.IndexOf(_keys, key);
            if (i >= 0 && !_fields.Rows[i].Cells[1].IsInEditMode && Value(i) == _saved[i])
            {
                _fields.Rows[i].Cells[1].Value = _saved[i] = value;
                UpdateFieldState(i);
            }
        }
        private void UpdateFieldState(int i)
        {
            bool automatic = _bound && BomNoteStore.IsAutomatic(_path, _keys[i], Value(i));
            var row = _fields.Rows[i];
            row.Cells[0].Value = (automatic ? "🔒 " : "") + _labels[i];
            row.Cells[1].ReadOnly = !_bound || automatic;
            row.DefaultCellStyle.BackColor = automatic || !_bound ? SystemColors.Control : Color.White;
            row.DefaultCellStyle.ForeColor = automatic || !_bound ? SystemColors.GrayText : SystemColors.ControlText;
            row.Cells[1].ToolTipText = automatic ? "自动读取，不可编辑" : "双击填写；购买网址可 Ctrl＋左键打开";
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { _fields.EndEdit(); Save(); BomNoteStore.Changed -= Changed; _linkFont.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
