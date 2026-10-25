using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MechKit.Core;
using MechKit.Features;

namespace MechKit.UI
{
    internal sealed partial class PartListForm
    {
        private readonly object _parentMenuTag = new object();

        internal static PartListRow FindLoadedNoteRow(string path, string configuration)
        {
            if (string.IsNullOrEmpty(path)) return null;
            foreach (Form open in Application.OpenForms)
            {
                var bom = open as PartListForm;
                if (bom == null || bom.IsDisposed) continue;
                foreach (var row in bom._rows)
                    if (string.Equals(row.FilePath, path, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(row.Configuration, configuration, StringComparison.OrdinalIgnoreCase)) return row;
            }
            return null;
        }

        private void ConfigureParentAssemblyMenu(ContextMenuStrip menu, DataGridViewCell cell)
        {
            for (int i = menu.Items.Count - 1; i >= 0; i--)
            {
                var old = menu.Items[i];
                if (!ReferenceEquals(old.Tag, _parentMenuTag)) continue;
                menu.Items.RemoveAt(i); old.Dispose();
            }
            if (cell == null) return;
            var row = cell.OwningRow.Tag as PartListRow;
            if (row == null) return;
            if (cell.OwningColumn.Name == "drawing" && row.Classification == "标准件")
            {
                var insert = new ToolStripMenuItem("插入说明书文件…") { Tag = _parentMenuTag, Enabled = !_running };
                insert.Click += delegate { InsertManual(row); };
                var open = new ToolStripMenuItem("打开说明书") { Tag = _parentMenuTag, Enabled = !_running && BomAssets.Manual(row.FilePath) != null };
                open.Click += delegate
                {
                    try { var path = BomAssets.Manual(row.FilePath); if (path != null) WindowLayout.OpenExternal(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
                    catch (Exception ex) { _status.Text = "打开说明书失败：" + ex.Message; }
                };
                menu.Items.Insert(0, insert); menu.Items.Insert(1, open);
                menu.Items.Insert(2, new ToolStripSeparator { Tag = _parentMenuTag });
                return;
            }
            if (cell.OwningColumn.Name != "location") return;
            HideModelPreview();
            var current = new ToolStripMenuItem("打开当前零件")
            {
                Enabled = !_running && !string.IsNullOrEmpty(row.FilePath),
                Tag = _parentMenuTag,
                ToolTipText = row.FilePath
            };
            current.Click += delegate { OpenMenuModel(row, "当前零件"); };
            menu.Items.Insert(0, current);
            for (int level = 0; level < 2; level++)
            {
                var target = row.ParentAssemblies != null && row.ParentAssemblies.Count > level ? row.ParentAssemblies[level] : null;
                string label = level == 0 ? "打开上一级装配体" : "打开上二级装配体";
                bool available = target != null && !string.IsNullOrEmpty(target.FilePath) &&
                    string.Equals(Path.GetExtension(target.FilePath), ".sldasm", StringComparison.OrdinalIgnoreCase);
                var item = new ToolStripMenuItem(label + (available ? "：" + Path.GetFileNameWithoutExtension(target.FilePath) : "（无可打开的装配体）"))
                {
                    Enabled = available && !_running,
                    Tag = _parentMenuTag,
                    ToolTipText = available ? target.FilePath : "该层级不存在，或装配体尚未保存为外部文件"
                };
                item.Click += delegate { OpenParentAssembly(target); };
                menu.Items.Insert(level + 1, item);
            }
            menu.Items.Insert(3, new ToolStripSeparator { Tag = _parentMenuTag });
        }

        private void OpenParentAssembly(PartListRow target)
        {
            OpenMenuModel(target, "父装配体");
        }

        private void InsertManual(PartListRow row)
        {
            if (_running) return;
            HideModelPreview();
            using (var picker = new OpenFileDialog { Title = "选择标准件说明书", Filter = "说明书文件|*.pdf;*.doc;*.docx;*.txt;*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    BomAssets.SaveManual(row.FilePath, picker.FileName);
                    foreach (DataGridViewRow display in _grid.Rows)
                    {
                        var item = display.Tag as PartListRow;
                        if (item != null && string.Equals(item.FilePath, row.FilePath, StringComparison.OrdinalIgnoreCase))
                            display.Cells["drawing"].Value = PartListService.DrawingCellText(item);
                    }
                    _status.Text = "说明书已保存到零件资产：" + Path.GetFileName(picker.FileName);
                }
                catch (Exception ex) { _status.Text = "插入说明书失败：" + ex.Message; }
            }
        }

        private void OpenMenuModel(PartListRow target, string description)
        {
            if (_running || target == null) return;
            using (OperationTrace.Begin("BOM 打开" + description + " " + target.FilePath))
            {
                var opened = PartListService.OpenRowModelDocument(_host.SwApp, target, AppendLog);
                if (string.IsNullOrEmpty(opened)) return;
                _status.Text = "已打开：" + Path.GetFileName(opened) + " · 按 Tab 返回 BOM";
                WindowLayout.YieldToOpenedContent(_host.MainWindowHandle);
            }
        }
    }
}
