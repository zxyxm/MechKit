using System;
using System.Drawing;
using System.Windows.Forms;
using MechKit.Features;

namespace MechKit.UI
{
    internal sealed class QuickAnnotationForm : Form
    {
        private readonly IAddinHost _host;
        private readonly Label _status;
        private readonly Button[] _annotationTools = new Button[4];
        private readonly TabControl _tabs = new TabControl();
        private readonly ComboBox _holeParameters = new ComboBox();
        private readonly string[][] _cells = new string[3][];
        private readonly DataGridView[] _grids = new DataGridView[3];

        public QuickAnnotationForm(IAddinHost host)
        {
            _host = host;
            Text = "公差助手 - MechKit（右键单元格自定义）";
            Font = Theme.Body;
            BackColor = Theme.Canvas;
            ShowInTaskbar = false;
            WindowLayout.Attach(this, host.Settings, new Size(540, 680), new Size(490, 560));
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            var tools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
            var toolNames = new[] { "智能尺寸", "公差助手", "孔标注", "销钉符号" };
            var toolCommands = new[] { AddinConstants.NativeSmartDimension, 0, AddinConstants.NativeHoleCallout, AddinConstants.NativeDowelPinSymbol };
            for (var i = 0; i < toolNames.Length; i++)
            {
                tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
                var button = i == 1 ? Theme.CreatePrimaryButton(toolNames[i]) : Theme.CreateSecondaryButton(toolNames[i]);
                button.Dock = DockStyle.Fill;
                button.Name = "annotationTool" + i;
                var command = toolCommands[i];
                var title = toolNames[i];
                button.Click += delegate
                {
                    if (command == 0)
                    {
                        _tabs.SelectedIndex = 0;
                        _status.Text = "选择尺寸，再点击预设写入公差；孔标注自动读取参数。";
                    }
                    else RunDrawingCommand(command, title);
                };
                _annotationTools[i] = button;
                tools.Controls.Add(button, i, 0);
            }
            layout.Controls.Add(tools, 0, 0);
            var hint = Theme.CreateValueLabel("选择工程图尺寸后左键写入，右键编辑预设；可多选。\r\n公差单位 mm；备注仅作预设标签。窗口可保持打开。");
            hint.Dock = DockStyle.Fill;
            layout.Controls.Add(hint, 0, 1);
            var holeBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            holeBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            holeBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            holeBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));
            var holeLabel = Theme.CreateValueLabel("孔标注参数："); holeLabel.Dock = DockStyle.Fill;
            holeBar.Controls.Add(holeLabel, 0, 0);
            _holeParameters.DropDownStyle = ComboBoxStyle.DropDownList;
            _holeParameters.Dock = DockStyle.Fill;
            holeBar.Controls.Add(_holeParameters, 1, 0);
            var read = Theme.CreateSecondaryButton("读取孔参数"); read.Dock = DockStyle.Fill;
            read.Click += delegate { RefreshHoleParameters(); };
            holeBar.Controls.Add(read, 2, 0);
            layout.Controls.Add(holeBar, 0, 2);
            _tabs.Dock = DockStyle.Fill;
            var names = new[] { "写入公差", "写入前缀", "写入后缀" };
            var saved = new[] { host.Settings.ToleranceCells, host.Settings.DimensionPrefixCells, host.Settings.DimensionSuffixCells };
            for (var mode = 0; mode < 3; mode++)
            {
                _cells[mode] = QuickAnnotationService.DecodeCells(saved[mode], mode);
                var page = new TabPage(names[mode]);
                var grid = new DataGridView
                {
                    Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                    AllowUserToResizeRows = false, AllowUserToResizeColumns = false, ReadOnly = true,
                    MultiSelect = false, RowHeadersVisible = false, ColumnHeadersVisible = false,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
                    ScrollBars = ScrollBars.None, SelectionMode = DataGridViewSelectionMode.CellSelect
                };
                grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
                grid.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 11f);
                grid.DefaultCellStyle.SelectionBackColor = Theme.Accent;
                grid.DefaultCellStyle.SelectionForeColor = Color.White;
                for (var col = 0; col < 5; col++) grid.Columns.Add("cell" + col, "");
                grid.Rows.Add(7);
                for (var row = 0; row < 7; row++)
                    grid.Rows[row].DefaultCellStyle.BackColor = row % 2 == 0 ? Color.White : Color.FromArgb(255, 253, 217);
                var capturedMode = mode;
                grid.CellMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e)
                {
                    if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                    var index = e.RowIndex * 5 + e.ColumnIndex;
                    if (e.Button == MouseButtons.Right) EditCell(capturedMode, index);
                    else if (e.Button == MouseButtons.Left && !string.IsNullOrEmpty(_cells[capturedMode][index]))
                        Apply(_cells[capturedMode][index], capturedMode);
                };
                grid.Resize += delegate { ResizeRows(grid); };
                _grids[mode] = grid;
                page.Controls.Add(grid);
                _tabs.TabPages.Add(page);
                RefreshCells(mode);
            }
            layout.Controls.Add(_tabs, 0, 3);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var clear = Theme.CreateSecondaryButton("清除选中尺寸公差"); clear.Dock = DockStyle.Fill;
            clear.Click += delegate { Apply("清除公差", 0); };
            footer.Controls.Add(clear, 0, 0);
            var restore = Theme.CreateSecondaryButton("恢复本页默认预设"); restore.Dock = DockStyle.Fill;
            restore.Click += delegate
            {
                try
                {
                    var mode = _tabs.SelectedIndex;
                    _cells[mode] = QuickAnnotationService.DefaultCells(mode);
                    SaveCells(); RefreshCells(mode); _status.Text = "本页已恢复默认预设。";
                }
                catch (Exception ex) { _status.Text = ex.Message; }
            };
            footer.Controls.Add(restore, 1, 0);
            _status = Theme.CreateValueLabel("普通尺寸可直接写入。孔标注自动读取参数；多个参数时会提示选择。角度尺寸跳过。");
            _status.Dock = DockStyle.Fill;
            footer.Controls.Add(_status, 0, 1); footer.SetColumnSpan(_status, 2);
            layout.Controls.Add(footer, 0, 4);
            Controls.Add(layout);
        }
        private void RunDrawingCommand(int command, string title)
        {
            try
            {
                var app = _host.SwApp;
                var doc = app == null ? null : app.IActiveDoc2;
                if (doc == null || doc.GetType() != Core.SwUtils.DocDrawing)
                { _status.Text = "请先打开工程图，再使用" + title + "。"; return; }
                _status.Text = app.RunCommand(command, "")
                    ? "已启动" + title + "，请在工程图中选择对象。"
                    : "未能启动" + title + "，请结束当前命令并检查所选对象。";
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }
        private static void ResizeRows(DataGridView grid)
        {
            var height = Math.Max(32, (grid.ClientSize.Height - 2) / 7);
            foreach (DataGridViewRow row in grid.Rows) row.Height = height;
        }
        private void RefreshCells(int mode)
        {
            for (var i = 0; i < QuickAnnotationService.CellCount; i++)
            {
                var cell = _grids[mode].Rows[i / 5].Cells[i % 5];
                var text = _cells[mode][i];
                try { cell.Value = mode == 0 && !string.IsNullOrEmpty(text) ? TolerancePreset.Parse(text).Label : text; }
                catch (FormatException) { cell.Value = "无效预设\r\n右键修改"; }
                cell.ToolTipText = (text ?? "空白预设") + "\r\n左键写入 · 右键自定义";
            }
            ResizeRows(_grids[mode]);
        }
        private void SaveCells()
        {
            _host.Settings.ToleranceCells = QuickAnnotationService.EncodeCells(_cells[0]);
            _host.Settings.DimensionPrefixCells = QuickAnnotationService.EncodeCells(_cells[1]);
            _host.Settings.DimensionSuffixCells = QuickAnnotationService.EncodeCells(_cells[2]);
            _host.Settings.Save();
        }
        private void RefreshHoleParameters()
        {
            try
            {
                var selected = (_holeParameters.SelectedItem as HoleParameter)?.Name;
                var parameters = QuickAnnotationService.GetHoleParameters(_host.SwApp);
                _holeParameters.Items.Clear();
                foreach (var parameter in parameters) _holeParameters.Items.Add(parameter);
                _holeParameters.SelectedIndex = -1;
                foreach (var parameter in parameters)
                    if (parameter.Name == selected) _holeParameters.SelectedItem = parameter;
                _status.Text = parameters.Count == 0 ? "请在工程图中选中孔标注后读取参数。" : "请选择要给公差的孔标注参数，再点击预设。";
                _holeParameters.DroppedDown = parameters.Count > 0;
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }
        private void Apply(string text, int mode)
        {
            try
            {
                var selected = (_holeParameters.SelectedItem as HoleParameter)?.Name;
                if (mode == 0)
                {
                    // Read the current selection on every click so a previous hole's parameter is not reused blindly.
                    TolerancePreset.Parse(text);
                    var parameters = QuickAnnotationService.GetHoleParameters(_host.SwApp);
                    selected = QuickAnnotationService.ResolveHoleParameter(parameters, selected);
                    _holeParameters.Items.Clear();
                    foreach (var parameter in parameters) _holeParameters.Items.Add(parameter);
                    if (parameters.Count > 1 && selected == null)
                    {
                        using (var picker = new Form())
                        {
                            picker.Text = "选择要给公差的孔参数";
                            picker.Font = Theme.Body;
                            picker.ClientSize = new Size(480, 130);
                            picker.FormBorderStyle = FormBorderStyle.FixedDialog;
                            picker.StartPosition = FormStartPosition.CenterParent;
                            picker.MinimizeBox = false; picker.MaximizeBox = false;
                            var hint = new Label { Text = "孔标注包含多个参数，请选择孔径或深度：", Location = new Point(12, 12), Size = new Size(450, 24) };
                            var choices = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(12, 42), Width = 450 };
                            foreach (var parameter in parameters) choices.Items.Add(parameter);
                            choices.SelectedIndex = 0;
                            var ok = new Button { Text = "写入公差", Location = new Point(275, 86), Width = 90, DialogResult = DialogResult.OK };
                            var cancel = new Button { Text = "取消", Location = new Point(375, 86), Width = 90, DialogResult = DialogResult.Cancel };
                            picker.Controls.AddRange(new Control[] { hint, choices, ok, cancel });
                            picker.AcceptButton = ok; picker.CancelButton = cancel;
                            if (picker.ShowDialog(this) != DialogResult.OK)
                            { _status.Text = "已取消，未写入公差。"; return; }
                            selected = ((HoleParameter)choices.SelectedItem).Name;
                        }
                    }
                    foreach (var parameter in parameters)
                        if (parameter.Name == selected) _holeParameters.SelectedItem = parameter;
                }
                _status.Text = QuickAnnotationService.Apply(_host.SwApp, text, mode, selected);
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }
        private void EditCell(int mode, int index)
        {
            using (var editor = new Form())
            {
                editor.Text = mode == 0 ? "自定义公差预设" : "自定义尺寸文字预设";
                editor.Font = Theme.Body; editor.ClientSize = new Size(520, 180);
                editor.FormBorderStyle = FormBorderStyle.FixedDialog;
                editor.MaximizeBox = false; editor.MinimizeBox = false; editor.StartPosition = FormStartPosition.CenterParent;
                var label = new Label { Location = new Point(14, 12), Size = new Size(490, 66), Text = mode == 0
                    ? "格式：上公差/下公差/备注，例 0.03/0/销钉\r\n也支持 ±0.02、H7、h7。备注只作标签，不写入尺寸。\r\n输入 0 或留空清空此格；修改后自动保存。"
                    : "输入要写入尺寸的前缀或后缀文字。\r\n输入 0 或留空清空此格；修改后自动保存。\r\n前缀、后缀用于普通尺寸，孔标注不支持。" };
                var input = new TextBox { Location = new Point(14, 84), Width = 490, Text = _cells[mode][index] ?? "" };
                var ok = new Button { Text = "保存", Location = new Point(330, 134), Width = 80 };
                var cancel = new Button { Text = "取消", Location = new Point(420, 134), Width = 80, DialogResult = DialogResult.Cancel };
                ok.Click += delegate
                {
                    try
                    {
                        var value = input.Text.Trim();
                        if (value == "0") value = "";
                        if (mode == 0 && value.Length > 0) TolerancePreset.Parse(value);
                        var previous = _cells[mode][index]; _cells[mode][index] = value;
                        try { SaveCells(); } catch { _cells[mode][index] = previous; throw; }
                        RefreshCells(mode); _status.Text = "预设已保存。";
                        editor.DialogResult = DialogResult.OK; editor.Close();
                    }
                    catch (Exception ex) { MessageBox.Show(editor, ex.Message, "预设未保存", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                };
                editor.Controls.AddRange(new Control[] { label, input, ok, cancel });
                editor.AcceptButton = ok; editor.CancelButton = cancel;
                editor.Shown += delegate { input.Focus(); input.SelectAll(); };
                editor.ShowDialog(this);
            }
        }
    }
}
