using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using MechKit.Core;
using MechKit.Features;

namespace MechKit.UI
{
    internal sealed partial class PartListForm
    {
        private void ConfigureSpreadsheetEditing()
        {
            _grid.MultiSelect = true;
            _grid.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Handled || _running || _grid.IsCurrentCellInEditMode) return;
                if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back)
                { e.Handled = e.SuppressKeyPress = true; RunSheetCommand(false); }
                else if (e.Control && e.KeyCode == Keys.V)
                { e.Handled = e.SuppressKeyPress = true; RunSheetCommand(true); }
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add("复制  Ctrl+C", null, delegate
            {
                try { var data = _grid.GetClipboardContent(); if (data != null) Clipboard.SetDataObject(data); }
                catch (Exception ex) { _status.Text = "复制失败：" + ex.Message; }
            });
            menu.Items.Add("粘贴  Ctrl+V", null, delegate { RunSheetCommand(true); });
            menu.Items.Add("清空选中内容  Delete", null, delegate { RunSheetCommand(false); });
            menu.Items.Add("全选  Ctrl+A", null, delegate { _grid.SelectAll(); });
            _grid.ContextMenuStrip = menu;
            _grid.CellMouseDown += delegate(object sender, DataGridViewCellMouseEventArgs e)
            {
                if (e.Button != MouseButtons.Right) return;
                ConfigureParentAssemblyMenu(menu, e.RowIndex >= 0 && e.ColumnIndex >= 0 ? _grid[e.ColumnIndex, e.RowIndex] : null);
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                var cell = _grid[e.ColumnIndex, e.RowIndex];
                if (!cell.Selected) { _grid.CurrentCell = cell; _grid.ClearSelection(); cell.Selected = true; }
            };
            _grid.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10))
                    ConfigureParentAssemblyMenu(menu, _grid.CurrentCell);
            };
            Disposed += delegate { menu.Dispose(); };
        }

        private void RunSheetCommand(bool paste)
        {
            if (_running || _grid.IsCurrentCellInEditMode) return;
            try
            {
                if (paste && _grid.CurrentCell != null && _grid.CurrentCell.OwningColumn.Name == "drawing" &&
                    (Clipboard.ContainsImage() || Clipboard.ContainsFileDropList()))
                { PasteBomIllustration(_grid, new KeyEventArgs(Keys.Control | Keys.V)); return; }
                if (paste && !Clipboard.ContainsText()) { _status.Text = "请先复制表格文本。"; return; }
                ApplySheetText(paste ? Clipboard.GetText() : null);
            }
            catch (Exception ex) { _status.Text = "表格操作未完成：" + ex.Message; }
        }

        // Excel TSV supports quoted tabs/newlines and doubled quotes.
        internal static List<List<string>> ParseSheetText(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>(); var value = new StringBuilder(); bool quoted = false;
            text = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"' && (quoted || value.Length == 0))
                {
                    if (quoted && i + 1 < text.Length && text[i + 1] == '"') { value.Append('"'); i++; }
                    else quoted = !quoted;
                }
                else if (!quoted && (c == '\t' || c == '\n'))
                {
                    row.Add(value.ToString()); value.Clear();
                    if (c == '\n') { rows.Add(row); row = new List<string>(); }
                }
                else value.Append(c);
            }
            if (quoted) throw new ArgumentException("粘贴内容的引号未闭合。");
            if (value.Length > 0 || row.Count > 0 || rows.Count == 0 || !text.EndsWith("\n"))
            { row.Add(value.ToString()); rows.Add(row); }
            return rows;
        }

        private static bool SheetEditable(DataGridViewCell cell)
        {
            return !cell.ReadOnly && cell.OwningRow.Tag is PartListRow &&
                cell is DataGridViewTextBoxCell && cell.OwningColumn.Name != "designTree" && cell.OwningColumn.Name != "fullname";
        }

        private void ApplySheetText(string text)
        {
            if (_running || _grid.CurrentCell == null) return;
            var edits = new Dictionary<DataGridViewCell, string>();
            var selected = _grid.SelectedCells.Cast<DataGridViewCell>().ToList();
            var values = text == null ? null : ParseSheetText(text);
            if (values == null || (values.Count == 1 && values[0].Count == 1))
            {
                foreach (var cell in selected) if (SheetEditable(cell))
                    edits[cell] = values == null ? "" : values[0][0];
            }
            else
            {
                var columns = _grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible)
                    .OrderBy(c => c.DisplayIndex).ToList();
                int start = columns.IndexOf(_grid.CurrentCell.OwningColumn);
                int firstRow = _grid.CurrentCell.RowIndex;
                if (firstRow + values.Count > _grid.Rows.Count || start + values.Max(r => r.Count) > columns.Count)
                    throw new ArgumentException("粘贴范围超出表格边界，请减少内容或更换起始单元格。");
                for (int r = 0; r < values.Count; r++)
                    for (int c = 0; c < values[r].Count; c++)
                    {
                        var cell = _grid[columns[start + c].Index, firstRow + r];
                        if (SheetEditable(cell)) edits[cell] = values[r][c];
                    }
            }
            // Validate all proposed document-level fields before mutating anything.
            var proposed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var edit in edits)
            {
                var row = (PartListRow)edit.Key.OwningRow.Tag;
                var key = edit.Key.OwningColumn.Name;
                var value = edit.Value.Trim();
                if (key == "purchaselink") PurchaseLinkService.Validate(value);
                string identity = (string.IsNullOrEmpty(row.FilePath) ? "row:" + edit.Key.RowIndex : row.FilePath) + "\0" + key;
                string previous;
                if (proposed.TryGetValue(identity, out previous) && previous != value)
                    throw new ArgumentException("同一零件的「" + edit.Key.OwningColumn.HeaderText + "」出现不同值，请统一后再粘贴。");
                proposed[identity] = value;
            }
            int changed = 0;
            foreach (var edit in edits)
            {
                var row = (PartListRow)edit.Key.OwningRow.Tag;
                var key = edit.Key.OwningColumn.Name; var value = edit.Value.Trim();
                if (IsInlinePropertyColumn(key)) BomNoteStore.Save(row.FilePath, key, value);
                foreach (var peer in _rows.Where(p => ReferenceEquals(p, row) ||
                    (!string.IsNullOrEmpty(row.FilePath) && string.Equals(p.FilePath, row.FilePath, StringComparison.OrdinalIgnoreCase))))
                {
                    switch (key)
                    {
                        case "name": peer.Name = value; break;
                        case "fullname": peer.FullName = value; break;
                        case "material": peer.Material = value; break;
                        case "process": peer.Process = value; break;
                        case "assemblynote": peer.AssemblyNote = value; break;
                        case "remark": peer.Remark = value; break;
                        case "unfinished": peer.Unfinished = value; break;
                        case "purchaselink": peer.PurchaseLink = value; break;
                    }
                }
                foreach (var peer in _rows.Where(p => ReferenceEquals(p, row) || string.Equals(p.FilePath, row.FilePath, StringComparison.OrdinalIgnoreCase)))
                    peer.MarkInlinePropertyClean(key);
                foreach (DataGridViewRow display in _grid.Rows)
                {
                    var peer = display.Tag as PartListRow;
                    if (peer != null && (ReferenceEquals(peer, row) || (!string.IsNullOrEmpty(row.FilePath) &&
                        string.Equals(peer.FilePath, row.FilePath, StringComparison.OrdinalIgnoreCase))))
                    { display.Cells[key].Value = value; display.Cells[key].ErrorText = ""; }
                }
                changed++;
            }
            _status.Text = string.Format("已处理 {0} 个可编辑单元格，只读列已跳过。同一零件同步；备注字段已保存到零部件资产文件夹。", changed);
        }
    }
}
