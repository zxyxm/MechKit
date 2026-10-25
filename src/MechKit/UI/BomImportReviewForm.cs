using System;
using System.Drawing;
using System.Windows.Forms;
using MechKit.Features;

namespace MechKit.UI
{
    internal sealed class BomImportReviewForm : Form
    {
        private readonly DataGridView _grid = new DataGridView();
        internal BomImportReviewForm(BomImportPlan plan)
        {
            Text = "导入 BOM · 匹配及字段差异";
            ClientSize = new Size(1100, 650); MinimumSize = new Size(800, 450);
            StartPosition = FormStartPosition.CenterParent;
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 65, Padding = new Padding(8), WrapContents = true };
            var mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
            mode.Items.AddRange(new object[] { "更新选中字段", "仅补充空白字段" }); mode.SelectedIndex = 0;
            top.Controls.Add(mode);
            top.Controls.Add(new Label { AutoSize = true, Padding = new Padding(8, 6, 0, 0),
                Text = plan.Linked ? "关联同步：仅备注字段写入资产；生成列改动需在模型中处理。冲突/清空须手工勾选。未匹配 " + plan.Unmatched + " 行。" : "数量只核对；空值不删除；同文件各实例同步。未匹配 " + plan.Unmatched + " 行。应用后仍需“应用 BOM 修改”保存。" });
            _grid.Dock = DockStyle.Fill; _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false;
            _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "apply", HeaderText = "更新", Width = 48 });
            foreach (var name in new[] { "零件", "字段", "当前值", "表格值", "处理说明" })
                _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, ReadOnly = true,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = name == "字段" ? 45 : 100 });
            foreach (var field in plan.Fields)
            {
                var row = _grid.Rows[_grid.Rows.Add(field.Selected, field.Part, field.Field, field.Before, field.After, field.Reason)];
                row.Tag = field; row.Cells[0].ReadOnly = !field.CanApply;
                if (field.RequiresConfirmation) row.DefaultCellStyle.BackColor = Color.LightYellow;
                if (!field.CanApply) row.DefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
            }
            foreach (var message in plan.Messages)
            {
                var row = _grid.Rows[_grid.Rows.Add(false, "未匹配", "", "", "", message)];
                row.ReadOnly = true; row.DefaultCellStyle.BackColor = Color.MistyRose;
            }
            mode.SelectedIndexChanged += delegate
            {
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    var field = row.Tag as BomImportField;
                    if (field != null) row.Cells[0].Value = field.CanApply && !field.RequiresConfirmation && (mode.SelectedIndex == 0 || field.EmptyBefore);
                }
            };
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 90 };
            var apply = new Button { Text = "应用选中字段", Width = 130 };
            apply.Click += delegate
            {
                _grid.EndEdit();
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    var field = row.Tag as BomImportField;
                    if (field != null) field.Selected = field.CanApply && Convert.ToBoolean(row.Cells[0].Value);
                }
                DialogResult = DialogResult.OK;
            };
            bottom.Controls.Add(cancel); bottom.Controls.Add(apply);
            Controls.Add(_grid); Controls.Add(top); Controls.Add(bottom);
            CancelButton = cancel;
        }
    }
}
