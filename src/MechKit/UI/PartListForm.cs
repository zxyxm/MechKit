using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using MechKit.Core;
using MechKit.Features;

namespace MechKit.UI
{
    /// <summary>
    /// BOM 预览：汇总零件后可直接编辑名称、材料和工艺，并写回零件属性。
    /// </summary>
    internal sealed class PartListForm : Form
    {
        private readonly IAddinHost _host;
        private readonly RadioButton _sourceDocument;
        private readonly Button _selectComponentButton;
        private readonly Button _clearComponentButton;
        private readonly ComboBox _subassemblyCombo;
        private readonly Button _reloadSubassembliesButton;
        private readonly Label _scopeLabel;
        private readonly ComboBox _partNumberSource;
        private readonly ComboBox _cutRule;
        private readonly TextBox _patternBox;
        private readonly ComboBox _materialSource;
        private readonly ComboBox _classificationFilter;
        private readonly CheckBox _onlyMachined;
        private readonly CheckBox _excludeToolbox;
        private readonly CheckBox _excludeSuppressed;
        private readonly CheckBox _readProperties;
        private readonly CheckBox _detectVendor;
        private readonly CheckBox _useSegments;
        private readonly TextBox _segmentSeparator;
        private readonly ComboBox _materialSegment;
        private readonly TextBox _partNumberProperty;
        private readonly TextBox _materialProperty;
        private readonly DataGridView _grid;
        private readonly TextBox _logBox;
        private readonly Label _status;
        private readonly Button _runButton;
        private readonly Button _exportButton;
        private readonly Button _exportFilesButton;
        private readonly CheckBox _selectAll;
        private readonly Button _writeBackButton;
        private readonly Button _renameComponentsButton;
        private readonly Button _closeButton;

        /// <summary>不符合命名规则的组件行使用深红色整行标出。</summary>
        private static readonly Color UnmatchedRowColor = Color.FromArgb(200, 33, 39);

        /// <summary>有二维工程图的行：浅绿底。</summary>
        private static readonly Color DrawingRowColor = Color.FromArgb(226, 245, 226);

        /// <summary>加工件缺二维工程图的行：浅红底。</summary>
        private static readonly Color MissingDrawingRowColor = Color.FromArgb(253, 230, 230);

        private List<PartListRow> _rows = new List<PartListRow>();
        private Component2 _selectedComponent;
        private string _selectedComponentName = string.Empty;
        private bool _changingScope;
        private bool _loadingSubassemblies;
        private bool _running;
        private bool _renameEditPending;

        public PartListForm(IAddinHost host)
        {
            _host = host;
            _sourceDocument = new RadioButton();
            _selectComponentButton = Theme.CreatePrimaryButton("选择部件");
            _clearComponentButton = Theme.CreateSecondaryButton("全部装配体");
            _subassemblyCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = Theme.Body,
                Width = 420,
                DropDownWidth = 620
            };
            _reloadSubassembliesButton = Theme.CreateSecondaryButton("读取子装配体");
            _scopeLabel = Theme.CreateValueLabel("BOM 范围：全部装配体");
            _partNumberSource = new ComboBox();
            _cutRule = new ComboBox();
            _patternBox = Theme.CreateTextBox();
            _materialSource = new ComboBox();
            _classificationFilter = new ComboBox();
            _onlyMachined = new CheckBox();
            _excludeToolbox = new CheckBox();
            _excludeSuppressed = new CheckBox();
            _readProperties = new CheckBox();
            _detectVendor = new CheckBox();
            _useSegments = new CheckBox();
            _segmentSeparator = Theme.CreateTextBox();
            _materialSegment = new ComboBox();
            _partNumberProperty = Theme.CreateTextBox();
            _materialProperty = Theme.CreateTextBox();
            _grid = new DataGridView();
            _logBox = new TextBox();
            _status = Theme.CreateValueLabel("就绪");
            _runButton = Theme.CreatePrimaryButton("刷新预览");
            _exportButton = Theme.CreateSecondaryButton("导出 CSV");
            _exportFilesButton = Theme.CreateSecondaryButton("导出选中件…");
            _selectAll = new CheckBox
            {
                Text = "全选",
                AutoSize = true,
                ForeColor = Theme.Text,
                BackColor = Theme.Canvas
            };
            _writeBackButton = Theme.CreatePrimaryButton("应用 BOM 修改");
            _renameComponentsButton = Theme.CreatePrimaryButton("按规则重命名文件");
            _closeButton = Theme.CreateSecondaryButton("关闭");

            BuildLayout();
            WireEvents();
            LoadDefaults();
        }

        private void BuildLayout()
        {
            Text = "预览 BOM - " + AddinConstants.Title;
            Font = Theme.Body;
            BackColor = Theme.Canvas;
            StartPosition = FormStartPosition.CenterParent;
            WindowLayout.Attach(this, _host.Settings, new Size(1080, 800), new Size(920, 640));

            Controls.Add(BuildResultPanel());
            Controls.Add(BuildSetupPanel());
            Controls.Add(BuildLogPanel());
            Controls.Add(BuildFooter());
            Controls.Add(BuildHeader());
        }

        private Control BuildHeader()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Theme.Accent };
            var title = Theme.CreateLabel("预览 BOM", Theme.Title, Color.White);
            title.Location = new Point(14, 9);

            var subtitle = Theme.CreateLabel("双击可编辑字段；列宽首次自动适配，之后可拖动表头边界调整",
                Theme.Small, Color.FromArgb(214, 232, 248));
            subtitle.Location = new Point(15, 32);

            panel.Controls.Add(title);
            panel.Controls.Add(subtitle);
            return panel;
        }

        private Control BuildSetupPanel()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 208, BackColor = Theme.Surface, Padding = new Padding(12, 8, 12, 6) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Theme.Surface
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            layout.Controls.Add(BuildSourceRow(), 0, 0);
            layout.Controls.Add(BuildSubassemblyRow(), 0, 1);
            layout.Controls.Add(BuildNamingSummaryRow(), 0, 2);
            layout.Controls.Add(BuildFilterRow(), 0, 3);

            panel.Controls.Add(layout);
            return panel;
        }

        private Control BuildSourceRow()
        {
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Surface
            };

            _sourceDocument.Text = "当前装配体";
            _sourceDocument.Font = Theme.Body;
            _sourceDocument.AutoSize = true;
            _sourceDocument.Margin = new Padding(0, 9, 12, 0);

            _selectComponentButton.Width = 104;
            _selectComponentButton.Margin = new Padding(0, 4, 8, 0);
            _clearComponentButton.Width = 104;
            _clearComponentButton.Margin = new Padding(0, 4, 12, 0);
            _scopeLabel.AutoSize = true;
            _scopeLabel.ForeColor = Theme.Text;
            _scopeLabel.Margin = new Padding(0, 10, 0, 0);

            var hint = Theme.CreateLabel("先在装配树或图形区选中零件/子装配体，再点「选择部件」",
                Theme.Small, Theme.Muted);
            hint.Margin = new Padding(16, 10, 0, 0);

            row.Controls.Add(_sourceDocument);
            row.Controls.Add(_selectComponentButton);
            row.Controls.Add(_clearComponentButton);
            row.Controls.Add(_scopeLabel);
            row.Controls.Add(hint);
            return row;
        }

        private Control BuildSubassemblyRow()
        {
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Surface,
                Padding = new Padding(28, 0, 0, 0)
            };

            var label = Theme.CreateFieldLabel("部件装配体");
            label.Width = 92;
            label.Margin = new Padding(0, 9, 8, 0);

            _subassemblyCombo.Margin = new Padding(0, 6, 8, 0);
            _reloadSubassembliesButton.Width = 118;
            _reloadSubassembliesButton.Margin = new Padding(0, 4, 10, 0);

            var hint = Theme.CreateLabel("选择后自动生成该部件的 BOM（包含其下级子装配体）",
                Theme.Small, Theme.Muted);
            hint.Margin = new Padding(0, 10, 0, 0);

            row.Controls.Add(label);
            row.Controls.Add(_subassemblyCombo);
            row.Controls.Add(_reloadSubassembliesButton);
            row.Controls.Add(hint);
            return row;
        }

        /// <summary>
        /// 命名规则摘要 + 设置按钮。具体规则集中在「命名规则设置」窗口里，
        /// 这里只显示当前规则并留一个入口。
        /// </summary>
        private Control BuildNamingSummaryRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Theme.Surface
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168f));

            var summary = Theme.CreateValueLabel(string.Empty);
            summary.Dock = DockStyle.Fill;
            summary.ForeColor = Theme.Muted;
            summary.Text = DescribeRule();
            _ruleSummary = summary;

            var button = Theme.CreatePrimaryButton("命名规则设置…");
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 2, 0, 2);
            button.Click += delegate
            {
                _host.ShowNamingRuleDialog(0, delegate { summary.Text = DescribeRule(); });
            };

            row.Controls.Add(summary, 0, 0);
            row.Controls.Add(button, 1, 0);
            return row;
        }

        private Label _ruleSummary;

        /// <summary>把当前命名规则写成人话。</summary>
        private string DescribeRule()
        {
            var s = _host.Settings;
            var separator = "-（兼容 _）";
            var prefixes = s.BomPrefixes;
            var segmentOrder = NamingOptionsFactory.DescribeMachinedSegments(
                NamingOptionsFactory.ParseMachinedSegments(s.MachinedSegments),
                NamingOptionsFactory.ParseMachinedSegmentLabels(s.MachinedSegmentLabels,
                    NamingOptionsFactory.ParseMachinedSegments(s.MachinedSegments).Length));

            // 子装配体的读取策略：整机外购件整体计入，只有按规则命名的装配体才继续往下读。
            var scope = s.BomRequirePattern
                ? "；子装配体 = 前缀开头按整机计入，其余需「日期-装配」命名才展开"
                : string.Empty;

            return string.Format(
                "当前规则：加工件 = {0}（分隔符 {1}）；标准件前缀 = {2}；参考件 = 参考-（不进入 BOM）{3}{4}",
                segmentOrder,
                separator,
                string.IsNullOrEmpty(prefixes) ? "（未设置）" : prefixes,
                s.BomRequirePattern ? string.Empty : "（当前已关闭过滤，全部收录）",
                scope);
        }

        private Control BuildNamingRow()
        {
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Surface
            };

            _partNumberSource.DropDownStyle = ComboBoxStyle.DropDownList;
            _partNumberSource.Font = Theme.Body;
            _partNumberSource.Width = 130;
            _partNumberSource.Items.AddRange(new object[] { "文件名", "自定义属性优先", "仅自定义属性" });
            _partNumberSource.Margin = new Padding(0, 5, 10, 0);

            _cutRule.DropDownStyle = ComboBoxStyle.DropDownList;
            _cutRule.Font = Theme.Body;
            _cutRule.Width = 130;
            _cutRule.Items.AddRange(new object[] { "完整文件名", "第一个空格前", "第一个下划线前", "第一个短横线前", "自定义正则" });
            _cutRule.Margin = new Padding(0, 5, 10, 0);

            _patternBox.Width = 190;
            _patternBox.Margin = new Padding(0, 5, 20, 0);

            _materialSource.DropDownStyle = ComboBoxStyle.DropDownList;
            _materialSource.Font = Theme.Body;
            _materialSource.Width = 130;
            _materialSource.Items.AddRange(new object[] { "SOLIDWORKS 材料", "自定义属性优先", "仅自定义属性" });
            _materialSource.Margin = new Padding(0, 5, 8, 0);

            row.Controls.Add(Theme.CreateFieldLabel("图号来源"));
            row.Controls.Add(_partNumberSource);
            row.Controls.Add(Theme.CreateFieldLabel("文件名截断"));
            row.Controls.Add(_cutRule);
            row.Controls.Add(_patternBox);
            row.Controls.Add(Theme.CreateFieldLabel("材料来源"));
            row.Controls.Add(_materialSource);

            foreach (Control control in row.Controls)
            {
                if (control is Label)
                {
                    control.AutoSize = true;
                    control.Margin = new Padding(0, 9, 8, 0);
                    control.ForeColor = Theme.Text;
                }
            }

            return row;
        }

        private Control BuildFilterRow()
        {
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = Theme.Surface
            };

            var filterLabel = Theme.CreateFieldLabel("类型筛选");
            filterLabel.AutoSize = true;
            filterLabel.Margin = new Padding(0, 6, 6, 0);
            _classificationFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            _classificationFilter.Font = Theme.Body;
            _classificationFilter.Width = 118;
            _classificationFilter.Margin = new Padding(0, 2, 18, 0);
            _classificationFilter.Items.AddRange(new object[]
            {
                "全部类型", "只看加工件", "只看标准件", "只看参考件", "只看未匹配"
            });

            _onlyMachined.Text = "只列加工件（排除标准件 / 外购件）";
            _excludeToolbox.Text = "Toolbox 零件视为标准件";
            _excludeSuppressed.Text = "忽略压缩的组件";
            _readProperties.Text = "读取零件自定义属性（较慢，但图号 / 材料更准）";
            _detectVendor.Text = "按厂商关键词识别外购件";

            foreach (var box in new[] { _onlyMachined, _excludeToolbox, _detectVendor, _excludeSuppressed, _readProperties })
            {
                box.Font = Theme.Body;
                box.AutoSize = true;
                box.Margin = new Padding(0, 4, 16, 0);
            }

            _useSegments.Text = "名称/材料按文件名分段：";
            _useSegments.Font = Theme.Body;
            _useSegments.AutoSize = true;
            _useSegments.Margin = new Padding(0, 4, 4, 0);

            _segmentSeparator.Width = 52;
            _segmentSeparator.ReadOnly = true;
            _segmentSeparator.TextAlign = HorizontalAlignment.Center;
            _segmentSeparator.Margin = new Padding(0, 2, 4, 0);

            _materialSegment.DropDownStyle = ComboBoxStyle.DropDownList;
            _materialSegment.Font = Theme.Body;
            _materialSegment.Width = 108;
            _materialSegment.Margin = new Padding(0, 2, 14, 0);
            _materialSegment.Items.AddRange(new object[] { "材料=倒数第二段", "材料=最后一段", "材料=倒数第三段", "材料不取自名称" });

            row.Controls.Add(filterLabel);
            row.Controls.Add(_classificationFilter);
            row.Controls.Add(_onlyMachined);
            row.Controls.Add(_excludeToolbox);
            row.Controls.Add(_excludeSuppressed);
            row.Controls.Add(_detectVendor);
            row.Controls.Add(_readProperties);
            row.Controls.Add(_useSegments);
            row.Controls.Add(_segmentSeparator);
            row.Controls.Add(_materialSegment);
            return row;
        }

        private Control BuildResultPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas, Padding = new Padding(12, 0, 12, 4) };

            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = false;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;
            // 双击任意一行打开该零件的图：优先同名的工程图，没有工程图时打开模型。
            _grid.CellDoubleClick += OnGridCellDoubleClick;
            _grid.CellEndEdit += OnGridCellEndEdit;
            // 勾选框点一下就要立即生效，不要等离开单元格才提交。
            _grid.CurrentCellDirtyStateChanged += delegate
            {
                if (_grid.IsCurrentCellDirty)
                {
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            // 在表格里选中零件后，选项卡上的快捷按钮直接改这些零件。
            _grid.SelectionChanged += delegate { PublishBomSelection(); };
            Theme.StyleGrid(_grid);

            // 表格左上角（行头与列头交汇处）放一个「表头设置」入口，
            // 点一下直接跳到「设置 → BOM 格式」改表头名称 / 字段来源 / 列顺序。
            _grid.RowHeadersVisible = true;
            _grid.RowHeadersWidth = Math.Max(88,
                TextRenderer.MeasureText("表头设置", Theme.BodyBold).Width + 24);
            _grid.TopLeftHeaderCell.Value = "表头设置";
            _grid.TopLeftHeaderCell.ToolTipText =
                "打开「设置 → BOM 格式」：表头名称、字段来源和列顺序都在那里改";
            _grid.MouseClick += OnGridMouseClick;
            _grid.MouseMove += OnGridMouseMove;

            var sequenceColumn = new DataGridViewTextBoxColumn { Name = "sequence", HeaderText = HeaderText(_host.Settings.BomSequenceHeader, "序号"), Width = 50, ReadOnly = true };
            var locationColumn = new DataGridViewTextBoxColumn { Name = "location", HeaderText = HeaderText(_host.Settings.BomLocationHeader, "位置"), Width = 220, ReadOnly = true };
            // 「三维名称」= 零件 / 装配体文件名，可直接编辑（双击文字即可改名）。
            var fullNameColumn = new DataGridViewTextBoxColumn
            {
                Name = "fullname",
                HeaderText = HeaderText(_host.Settings.BomFullNameHeader, "三维名称") + "（可编辑）",
                Width = 210
            };
            var drawingColumn = new DataGridViewTextBoxColumn
            {
                Name = "drawing",
                HeaderText = HeaderText(_host.Settings.BomDrawingHeader, "二维工程图"),
                Width = 170,
                ReadOnly = true
            };
            var typeColumn = new DataGridViewTextBoxColumn { Name = "classification", HeaderText = HeaderText(_host.Settings.BomClassificationHeader, "属性"), Width = 76, ReadOnly = true };
            var nameColumn = new DataGridViewTextBoxColumn
            {
                Name = "name",
                HeaderText = HeaderText(_host.Settings.BomNameHeader, "零件名称/标准件名称") + "（可编辑）",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Width = 190,
                MinimumWidth = 150
            };
            var materialColumn = new DataGridViewTextBoxColumn { Name = "material", HeaderText = HeaderText(_host.Settings.BomMaterialHeader, "材料/型号") + "（可编辑）", Width = 130 };
            var processColumn = new DataGridViewTextBoxColumn { Name = "process", HeaderText = HeaderText(_host.Settings.BomProcessHeader, "工艺/渠道") + "（可编辑）", Width = 130 };
            var surfaceColumn = new DataGridViewTextBoxColumn { Name = "surface", HeaderText = HeaderText(_host.Settings.BomSurfaceHeader, "表面处理"), Width = 110, ReadOnly = true };
            var quantityColumn = new DataGridViewTextBoxColumn { Name = "quantity", HeaderText = HeaderText(_host.Settings.BomQuantityHeader, "数量"), Width = 60, ReadOnly = true };
            var assemblyNoteColumn = new DataGridViewTextBoxColumn { Name = "assemblynote", HeaderText = HeaderText(_host.Settings.BomAssemblyNoteHeader, "安装说明"), Width = 120, ReadOnly = true };
            var remarkColumn = new DataGridViewTextBoxColumn { Name = "remark", HeaderText = HeaderText(_host.Settings.BomRemarkHeader, "备注") + "（可编辑）", Width = 150 };

            var editableColor = Color.FromArgb(255, 252, 226);
            nameColumn.DefaultCellStyle.BackColor = editableColor;
            materialColumn.DefaultCellStyle.BackColor = editableColor;
            processColumn.DefaultCellStyle.BackColor = editableColor;
            remarkColumn.DefaultCellStyle.BackColor = editableColor;
            fullNameColumn.DefaultCellStyle.BackColor = editableColor;

            var columns = new Dictionary<string, DataGridViewColumn>(StringComparer.OrdinalIgnoreCase)
            {
                { "sequence", sequenceColumn }, { "location", locationColumn },
                { "fullname", fullNameColumn }, { "drawing", drawingColumn },
                { "classification", typeColumn }, { "name", nameColumn },
                { "material", materialColumn }, { "process", processColumn },
                { "surface", surfaceColumn }, { "quantity", quantityColumn },
                { "assemblynote", assemblyNoteColumn }, { "remark", remarkColumn }
            };

            // 第一列固定为“导出勾选”：勾中的行可以直接送进批量导出（PDF / DWG / STEP…）。
            var selectColumn = new DataGridViewCheckBoxColumn
            {
                Name = "select",
                HeaderText = "导出",
                Width = Math.Max(46, TextRenderer.MeasureText("导出", Theme.BodyBold).Width + 26),
                MinimumWidth = 40,
                Resizable = DataGridViewTriState.False,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                FalseValue = false,
                TrueValue = true
            };
            _grid.Columns.Add(selectColumn);

            foreach (var key in PartListService.ParseColumnOrder(_host.Settings.BomColumnOrder))
            {
                _grid.Columns.Add(columns[key]);
            }

            panel.Controls.Add(_grid);
            return panel;
        }

        private static string HeaderText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private Control BuildLogPanel()
        {
            var panel = new Panel { Dock = DockStyle.Bottom, Height = 116, BackColor = Theme.Canvas, Padding = new Padding(12, 0, 12, 0) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Theme.Canvas
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            layout.Controls.Add(Theme.CreateLabel("处理日志", Theme.BodyBold, Theme.Text), 0, 0);
            Theme.StyleLogBox(_logBox);
            _logBox.Dock = DockStyle.Fill;
            layout.Controls.Add(_logBox, 0, 1);

            panel.Controls.Add(layout);
            return panel;
        }

        private Control BuildFooter()
        {
            var panel = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Canvas, Padding = new Padding(12, 8, 12, 8) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 8,
                RowCount = 1,
                BackColor = Theme.Canvas
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84f));

            _status.Dock = DockStyle.Fill;
            _status.Margin = new Padding(0);
            layout.Controls.Add(_status, 0, 0);

            foreach (var button in new[]
            {
                _runButton, _exportButton, _exportFilesButton, _writeBackButton,
                _renameComponentsButton, _closeButton
            })
            {
                button.Dock = DockStyle.Fill;
                button.Margin = new Padding(4, 8, 4, 8);
            }

            _selectAll.Dock = DockStyle.Fill;
            _selectAll.Margin = new Padding(4, 0, 8, 0);
            _selectAll.TextAlign = ContentAlignment.MiddleLeft;

            layout.Controls.Add(_selectAll, 1, 0);
            layout.Controls.Add(_runButton, 2, 0);
            layout.Controls.Add(_exportButton, 3, 0);
            layout.Controls.Add(_exportFilesButton, 4, 0);
            layout.Controls.Add(_writeBackButton, 5, 0);
            layout.Controls.Add(_renameComponentsButton, 6, 0);
            layout.Controls.Add(_closeButton, 7, 0);

            panel.Controls.Add(layout);
            return panel;
        }

        private void WireEvents()
        {
            _runButton.Click += delegate { RunSummary(); };
            _selectComponentButton.Click += delegate { SelectComponentScope(); };
            _clearComponentButton.Click += delegate { ClearComponentScope(true); };
            _reloadSubassembliesButton.Click += delegate { ReloadSubassemblies(); };
            _subassemblyCombo.SelectedIndexChanged += delegate { SelectSubassemblyScope(); };
            _exportButton.Click += delegate { ExportCsv(); };
            _exportFilesButton.Click += delegate { ExportSelectedFiles(); };
            _selectAll.CheckedChanged += delegate { SetAllRowsSelected(_selectAll.Checked); };
            _writeBackButton.Click += delegate { ApplyBomChanges(); };
            _renameComponentsButton.Click += delegate { ApplyBomChangesAndRename(); };
            _closeButton.Click += delegate { Close(); };

            _sourceDocument.CheckedChanged += delegate
            {
                if (_sourceDocument.Checked && !_changingScope)
                {
                    ClearComponentScope(false);
                }
                UpdateSourceState();
            };
            _cutRule.SelectedIndexChanged += delegate { UpdateSourceState(); };
            _classificationFilter.SelectedIndexChanged += delegate
            {
                if (_classificationFilter.SelectedIndex < 0)
                {
                    return;
                }
                PullGridEdits();
                ShowFilteredRows(false);
            };

            Shown += delegate
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    ReloadSubassemblies();
                    RunSummary();
                });
            };

            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (_running)
                {
                    MessageBox.Show(this, "汇总正在进行，请等待本次处理结束。", AddinConstants.Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    e.Cancel = true;
                }
            };
        }

        private void LoadDefaults()
        {
            var settings = _host.Settings;

            _sourceDocument.Checked = true;

            _patternBox.Text = settings.PartNumberPattern;

            _onlyMachined.Checked = false;
            _onlyMachined.Visible = false;
            _classificationFilter.SelectedIndex = 0;
            _excludeToolbox.Checked = true;
            _excludeSuppressed.Checked = true;
            _readProperties.Checked = settings.BomUsePropertyFields && settings.PartListReadProperties;
            _readProperties.Enabled = settings.BomUsePropertyFields;
            _detectVendor.Checked = settings.DetectVendorParts;
            _useSegments.Checked = settings.UseNameSegments;
            _segmentSeparator.Text = "-（兼容 _）";

            _partNumberProperty.Text = settings.PartNumberProperty;
            _materialProperty.Text = settings.MaterialProperty;

            _exportButton.Enabled = false;
            _writeBackButton.Enabled = false;
            _renameComponentsButton.Enabled = false;

            _subassemblyCombo.Items.Add(new SubassemblyScope { DisplayName = "全部装配体" });
            _subassemblyCombo.SelectedIndex = 0;

            UpdateSourceState();
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        private static int MaterialSegmentIndexFromValue(int segment)
        {
            switch (segment)
            {
                case -1:
                    return 1;
                case -3:
                    return 2;
                case 0:
                    return 3;
                default:
                    return 0;
            }
        }

        private static int MaterialSegmentValueFromIndex(int index)
        {
            switch (index)
            {
                case 1:
                    return -1;
                case 2:
                    return -3;
                case 3:
                    return 0;
                default:
                    return -2;
            }
        }

        private void UpdateSourceState()
        {
            _selectComponentButton.Enabled = !_running && _host.SwApp != null;
            _clearComponentButton.Enabled = !_running && _selectedComponent != null;
            _reloadSubassembliesButton.Enabled = !_running && _host.SwApp != null;
            _subassemblyCombo.Enabled = !_running && _subassemblyCombo.Items.Count > 1;
            _patternBox.Enabled = _cutRule.SelectedIndex == 4;
            _patternBox.BackColor = _patternBox.Enabled ? Color.White : Color.FromArgb(242, 243, 245);
            _renameComponentsButton.Enabled = !_running &&
                                              FilteredRows().Count > 0 && _host.SwApp != null;
        }

        private void SelectComponentScope()
        {
            if (_running)
            {
                return;
            }

            var doc = SwUtils.ActiveDoc(_host.SwApp);
            if (doc == null || doc.GetType() != SwUtils.DocAssembly)
            {
                MessageBox.Show(this, "请先打开装配体，并在装配树或图形区选中一个零件或子装配体。",
                    AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selection = doc.SelectionManager as SelectionMgr;
            Component2 component = null;
            if (selection != null)
            {
                for (var i = 1; i <= selection.GetSelectedObjectCount2(-1); i++)
                {
                    try
                    {
                        component = selection.GetSelectedObject6(i, -1) as Component2;
                    }
                    catch
                    {
                        component = null;
                    }
                    if (component != null)
                    {
                        break;
                    }
                }
            }

            if (component == null)
            {
                MessageBox.Show(this, "没有检测到已选部件。请先在装配树或图形区选中零件/子装配体，再点“选择部件”。",
                    AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var displayName = PartListService.DisplayNameForComponent(component);
            _loadingSubassemblies = true;
            try
            {
                for (var index = 1; index < _subassemblyCombo.Items.Count; index++)
                {
                    var scope = _subassemblyCombo.Items[index] as SubassemblyScope;
                    if (scope != null && IsSameComponent(scope.Component, component))
                    {
                        _subassemblyCombo.SelectedIndex = index;
                        displayName = scope.DisplayName;
                        break;
                    }
                }
            }
            finally
            {
                _loadingSubassemblies = false;
            }

            ApplyComponentScope(component, displayName, true);
        }

        private void ReloadSubassemblies()
        {
            if (_running)
            {
                return;
            }

            _loadingSubassemblies = true;
            try
            {
                _subassemblyCombo.Items.Clear();
                _subassemblyCombo.Items.Add(new SubassemblyScope { DisplayName = "全部装配体" });

                if (_host.SwApp == null)
                {
                    _subassemblyCombo.Items.Add(new SubassemblyScope { DisplayName = "离线示例：机架组件" });
                    _subassemblyCombo.Items.Add(new SubassemblyScope { DisplayName = "离线示例：驱动组件" });
                    _subassemblyCombo.SelectedIndex = 0;
                    return;
                }

                var doc = SwUtils.ActiveDoc(_host.SwApp);
                var assembly = doc as AssemblyDoc;
                if (doc == null || doc.GetType() != SwUtils.DocAssembly || assembly == null)
                {
                    _subassemblyCombo.SelectedIndex = 0;
                    return;
                }

                var scopes = PartListService.GetSubassemblies(assembly);
                var selectedIndex = 0;
                foreach (var scope in scopes)
                {
                    _subassemblyCombo.Items.Add(scope);
                    if (_selectedComponent != null && IsSameComponent(scope.Component, _selectedComponent))
                    {
                        selectedIndex = _subassemblyCombo.Items.Count - 1;
                    }
                }

                _subassemblyCombo.SelectedIndex = selectedIndex;
                AppendLog(string.Format("已读取子装配体：{0} 个。", scopes.Count));
            }
            finally
            {
                _loadingSubassemblies = false;
                UpdateSourceState();
            }
        }

        private void SelectSubassemblyScope()
        {
            if (_loadingSubassemblies || _running || _subassemblyCombo.SelectedIndex < 0)
            {
                return;
            }

            if (_subassemblyCombo.SelectedIndex == 0)
            {
                if (_selectedComponent != null)
                {
                    ClearComponentScope(true);
                }
                return;
            }

            var scope = _subassemblyCombo.SelectedItem as SubassemblyScope;
            if (scope == null || scope.Component == null)
            {
                return;
            }

            ApplyComponentScope(scope.Component, scope.DisplayName, true);
        }

        private void ApplyComponentScope(Component2 component, string displayName, bool refresh)
        {
            _changingScope = true;
            try
            {
                _sourceDocument.Checked = true;
                _selectedComponent = component;
                _selectedComponentName = string.IsNullOrWhiteSpace(displayName)
                    ? PartListService.DisplayNameForComponent(component)
                    : displayName;
            }
            finally
            {
                _changingScope = false;
            }

            _scopeLabel.Text = "BOM 范围：" +
                               (string.IsNullOrWhiteSpace(_selectedComponentName)
                                   ? "选中部件"
                                   : _selectedComponentName);
            _exportButton.Text = "导出部件 BOM";
            UpdateSourceState();
            if (refresh)
            {
                RunSummary();
            }
        }

        private static bool IsSameComponent(Component2 left, Component2 right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            if (ReferenceEquals(left, right))
            {
                return true;
            }

            try
            {
                return string.Equals(left.Name2, right.Name2, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(left.GetPathName(), right.GetPathName(), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void ClearComponentScope(bool refresh)
        {
            _selectedComponent = null;
            _selectedComponentName = string.Empty;
            _scopeLabel.Text = "BOM 范围：全部装配体";
            _exportButton.Text = "导出 CSV";

            if (!_loadingSubassemblies && _subassemblyCombo.Items.Count > 0)
            {
                _loadingSubassemblies = true;
                try { _subassemblyCombo.SelectedIndex = 0; }
                finally { _loadingSubassemblies = false; }
            }

            _sourceDocument.Checked = true;

            UpdateSourceState();
            if (refresh && IsHandleCreated && _host.SwApp != null)
            {
                RunSummary();
            }
        }

        private PartListOptions BuildOptions()
        {
            var options = PartListService.CreateOptions(_host.Settings);
            options.OnlyMachined = false;
            options.ExcludeToolbox = _excludeToolbox.Checked;
            options.DetectVendorParts = _detectVendor.Checked;
            options.ExcludeSuppressed = _excludeSuppressed.Checked;
            options.ReadCustomProperties = _readProperties.Checked;
            return options;
        }

        private void RunSummary()
        {
            if (_running)
            {
                return;
            }

            var options = BuildOptions();
            var swApp = _host.SwApp;
            if (swApp == null)
            {
                // 离线测试宿主：展示与真实 BOM 完全相同的列和典型解析结果。
                _rows = BuildOfflinePreviewRows();
                ShowFilteredRows(true);
                AppendLog("离线示例：20260919-6061-扫码枪安装板 → 加工件 / 扫码枪安装板 / 6061");
                AppendLog("离线示例：代理-电机-MG996 → 标准件 / 电机 / MG996 / 代理");
                _writeBackButton.Enabled = false;
                _renameComponentsButton.Enabled = false;
                return;
            }

            _running = true;
            _runButton.Enabled = false;
            _exportButton.Enabled = false;
            _writeBackButton.Enabled = false;
            _renameComponentsButton.Enabled = false;
            _logBox.Clear();
            _rows = new List<PartListRow>();
            _grid.Rows.Clear();

            try
            {
                AppendLog("规则：" + options.Naming.Describe());
                AppendLog("提示：双击表格任意一行，可打开该零件的工程图（没有工程图时打开零件模型）。");

                {
                    var doc = SwUtils.ActiveDoc(swApp);
                    if (doc == null)
                    {
                        MessageBox.Show(this, "请先打开一个装配体或零件。", AddinConstants.Title,
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    var docType = doc.GetType();
                    if (docType == SwUtils.DocAssembly)
                    {
                        var assembly = doc as AssemblyDoc;
                        if (assembly == null)
                        {
                            MessageBox.Show(this, "无法访问装配体接口。", AddinConstants.Title,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        if (_selectedComponent != null)
                        {
                            AppendLog("统计选中部件：" + _selectedComponentName);
                            _rows = PartListService.FromComponent(
                                swApp, _selectedComponent, options, AppendLog);
                        }
                        else
                        {
                            AppendLog("统计装配体：" + Path.GetFileName(doc.GetPathName()));
                            _rows = PartListService.FromAssembly(swApp, assembly, options, AppendLog);
                        }
                    }
                    else if (docType == SwUtils.DocPart)
                    {
                        AppendLog("读取零件：" + Path.GetFileName(doc.GetPathName()));
                        var row = PartListService.FromPart(swApp, doc, options);
                        if (row != null)
                        {
                            _rows.Add(row);
                        }
                    }
                    else
                    {
                        MessageBox.Show(this, "当前文档不是零件或装配体。", AddinConstants.Title,
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                }

                ShowFilteredRows(true);
                var summary = PartListService.BuildSummary(_rows);
                AppendLog(summary);
                Log.Info("明细汇总完成：" + summary);
            }
            catch (Exception ex)
            {
                Log.Error("明细汇总失败", ex);
                MessageBox.Show(this, "汇总过程出错：" + ex.Message, AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _running = false;
                _runButton.Enabled = true;
                ShowFilteredRows(false);
                UpdateSourceState();
            }
        }

        private static List<PartListRow> BuildOfflinePreviewRows()
        {
            return new List<PartListRow>
            {
                new PartListRow
                {
                    AssemblyNote = "机架组件",
                    Location = "顶层装配体 > 机架组件",
                    FullName = "20260919-6061-扫码枪安装板",
                    Classification = "加工件",
                    Name = "扫码枪安装板",
                    Material = "6061",
                    Process = "CNC",
                    SurfaceTreatment = "阳极氧化",
                    Quantity = 2,
                    Remark = "20260919-6061-扫码枪安装板"
                },
                new PartListRow
                {
                    AssemblyNote = "驱动组件",
                    Location = "顶层装配体 > 驱动组件",
                    FullName = "代理-电机-MG996",
                    Classification = "标准件",
                    Name = "伺服电机",
                    Material = "MG996",
                    Process = "电机",
                    SurfaceTreatment = string.Empty,
                    Quantity = 4,
                    Remark = "代理-电机-MG996"
                },
                new PartListRow
                {
                    AssemblyNote = "电控组件",
                    Location = "顶层装配体 > 电控组件",
                    FullName = "代理-接近开关-LJ12A3",
                    Classification = "标准件",
                    Name = "接近开关",
                    Material = "LJ12A3",
                    Process = "电气",
                    SurfaceTreatment = string.Empty,
                    Quantity = 3,
                    Remark = "代理-接近开关-LJ12A3"
                }
            };
        }

        /// <summary>
        /// 双击表格任意一行：打开该零件的工程图（同目录同名 .slddrw）；
        /// 没有工程图时退回打开零件 / 装配体模型。
        /// 单元格编辑仍可用单击选中后直接输入或按 F2。
        /// </summary>
        private void OnGridCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || _running)
            {
                return;
            }

            var row = _grid.Rows[e.RowIndex].Tag as PartListRow;
            if (row == null)
            {
                return;
            }

            var columnName = _grid.Columns[e.ColumnIndex].Name;
            var onText = IsDoubleClickOnCellText(e);

            // 双击名字文字 → 直接在单元格里改名字（提交后按新名字重命名文件）。
            if (onText && (string.Equals(columnName, "fullname", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(columnName, "name", StringComparison.OrdinalIgnoreCase)))
            {
                BeginRenameEdit(e.RowIndex, e.ColumnIndex);
                return;
            }

            // 双击文字后面的空白（或其它列） → 打开这一行的三维模型；
            // 三维文件打不开时再退回工程图。
            var opened = PartListService.OpenRowModelDocument(_host.SwApp, row, AppendLog);
            if (string.IsNullOrEmpty(opened))
            {
                opened = PartListService.OpenRowDocument(_host.SwApp, row, AppendLog);
            }

            if (string.IsNullOrEmpty(opened))
            {
                MessageBox.Show(this,
                    "这一行没有可打开的零件文件或工程图。\r\n\r\n" +
                    "· 双击名字文字 = 在表格里改名字；双击后面的空白 = 打开三维模型；\r\n" +
                    "· 虚拟件 / 未保存的零件没有磁盘文件；\r\n" +
                    "· 工程图需要与零件同目录、同文件名（.slddrw）。",
                    AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _status.Text = "已打开：" + Path.GetFileName(opened);
        }

        /// <summary>双击是否落在单元格的文字上（而不是文字后面的空白）。</summary>
        private bool IsDoubleClickOnCellText(DataGridViewCellEventArgs e)
        {
            try
            {
                var cell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                var text = Convert.ToString(cell.Value) ?? string.Empty;
                if (text.Length == 0)
                {
                    return false;
                }

                var bounds = _grid.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
                var point = _grid.PointToClient(Cursor.Position);
                if (!bounds.Contains(point))
                {
                    return false;
                }

                var style = cell.InheritedStyle;
                var font = style.Font ?? Theme.Body;
                var width = TextRenderer.MeasureText(text, font).Width;
                var left = bounds.Left + style.Padding.Left + 8;
                return point.X <= left + width + 4;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>双击名字后进入编辑；编辑提交时会问一次是否同步重命名文件。</summary>
        private void BeginRenameEdit(int rowIndex, int columnIndex)
        {
            try
            {
                _renameEditPending = true;
                _grid.CurrentCell = _grid.Rows[rowIndex].Cells[columnIndex];
                _grid.BeginEdit(true);
            }
            catch (Exception ex)
            {
                _renameEditPending = false;
                Log.Warn("进入改名编辑失败：" + ex.Message);
            }
        }

        private void OnGridCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (!_renameEditPending)
            {
                return;
            }

            _renameEditPending = false;
            PullGridEdits();
            var row = e.RowIndex >= 0 ? _grid.Rows[e.RowIndex].Tag as PartListRow : null;
            PromptRenameRow(row);
        }

        /// <summary>把这一行改成的新名字同步到零件文件（SOLIDWORKS 重命名接口，保持装配关系）。</summary>
        private void PromptRenameRow(PartListRow row)
        {
            if (_running || row == null || !row.HasFullNameEdit)
            {
                return;
            }

            if (row.IsUnmatched || string.IsNullOrEmpty(row.FilePath))
            {
                AppendLog("这一行没有可重命名的零件文件，名字只保留在表格里。");
                return;
            }

            var newBase = NamingOptions.GetFileNameWithoutExtension(row.FullName).Trim();
            var currentBase = Path.GetFileNameWithoutExtension(row.FilePath);
            if (newBase.Length == 0 || string.Equals(newBase, currentBase, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var answer = MessageBox.Show(this,
                "把这一行的零件 / 装配体文件重命名，并保持装配关系？\r\n\r\n" +
                currentBase + "  →  " + newBase + "\r\n\r\n" +
                "· “是”＝现在就重命名文件（引用同步更新）；\r\n" +
                "· “否”＝只保留表格里的名字，稍后点「按规则重命名文件」再统一执行。",
                AddinConstants.Title, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                if (answer == DialogResult.Cancel)
                {
                    row.FullName = currentBase;
                    ShowRows(FilteredRows(), false);
                }
                return;
            }

            try
            {
                var renamed = PartListService.RenameAssemblyFilesByBomRules(_host.SwApp,
                    new List<PartListRow> { row }, NamingOptionsFactory.FromSettings(_host.Settings), AppendLog);
                _status.Text = renamed > 0
                    ? "已重命名：" + newBase
                    : "重命名未执行（详见处理日志）。";
                ShowFilteredRows(false);
            }
            catch (Exception ex)
            {
                Log.Error("按名字重命名失败", ex);
                MessageBox.Show(this, "重命名失败：" + ex.Message, AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>表格左上角「表头设置」的热区：行头宽度 × 列头高度。</summary>
        private Rectangle GridHeaderCorner()
        {
            return new Rectangle(0, 0, _grid.RowHeadersWidth, _grid.ColumnHeadersHeight);
        }

        private void OnGridMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !_grid.RowHeadersVisible)
            {
                return;
            }

            if (GridHeaderCorner().Contains(e.Location))
            {
                _host.ShowSettingsDialog(0);
            }
        }

        private void OnGridMouseMove(object sender, MouseEventArgs e)
        {
            if (!_grid.RowHeadersVisible)
            {
                return;
            }

            var wanted = GridHeaderCorner().Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            if (_grid.Cursor != wanted)
            {
                _grid.Cursor = wanted;
            }
        }

        /// <summary>把表格里选中的行对应的零件文件告诉插件（供选项卡快捷按钮使用）。</summary>
        private void PublishBomSelection()
        {
            try
            {
                var paths = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (DataGridViewCell cell in _grid.SelectedCells)
                {
                    if (cell.RowIndex < 0 || cell.RowIndex >= _grid.Rows.Count)
                    {
                        continue;
                    }

                    var row = _grid.Rows[cell.RowIndex].Tag as PartListRow;
                    if (row == null || string.IsNullOrEmpty(row.FilePath))
                    {
                        continue;
                    }
                    if (seen.Add(row.FilePath))
                    {
                        paths.Add(row.FilePath);
                    }
                }

                _host.SetBomSelectedFiles(paths);
            }
            catch (Exception ex)
            {
                Log.Warn("同步 BOM 选中失败：" + ex.Message);
            }
        }

        /// <summary>勾选 / 取消勾选当前表格里的所有行。</summary>
        private void SetAllRowsSelected(bool selected)
        {
            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                gridRow.Cells["select"].Value = selected;
            }
        }

        private static bool IsRowMarkedForExport(DataGridViewRow gridRow)
        {
            if (gridRow == null)
            {
                return false;
            }

            var value = gridRow.Cells["select"].Value;
            return value != null && value != DBNull.Value && Convert.ToBoolean(value);
        }

        /// <summary>导出目标：优先同名工程图（PDF / DWG 需要工程图），没有就导出零件 / 装配体本身。</summary>
        private static string ResolveExportTarget(PartListRow row)
        {
            if (row == null || string.IsNullOrEmpty(row.FilePath))
            {
                return string.Empty;
            }

            var drawing = PartListService.FindDrawingFor(row.FilePath);
            if (!string.IsNullOrEmpty(drawing))
            {
                return drawing;
            }

            return row.FilePath;
        }

        /// <summary>把表格里勾选的行送进批量导出（PDF / DWG / DXF / STEP / IGES / STL）。</summary>
        private void ExportSelectedFiles()
        {
            PullGridEdits();
            var files = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                if (!IsRowMarkedForExport(gridRow))
                {
                    continue;
                }

                var target = ResolveExportTarget(gridRow.Tag as PartListRow);
                if (string.IsNullOrEmpty(target) || !File.Exists(target) || !seen.Add(target))
                {
                    continue;
                }

                files.Add(target);
            }

            if (files.Count == 0)
            {
                MessageBox.Show(this,
                    "请先在最左侧的「导出」列勾选要导出的行。\r\n\r\n" +
                    "· 勾选后点“导出选中件…”即可选择格式（PDF / DWG / DXF / STEP / IGES / STL）和输出目录；\r\n" +
                    "· 有同名工程图的零件会自动导出它的工程图，没有工程图的按零件 / 装配体导出。",
                    AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            AppendLog(string.Format("已把勾选的 {0} 个文件送进批量导出。", files.Count));
            _host.ShowBatchExportDialog(files);
        }

        private List<PartListRow> FilteredRows()
        {
            var result = new List<PartListRow>();
            var classification = string.Empty;
            switch (_classificationFilter.SelectedIndex)
            {
                case 1: classification = "加工件"; break;
                case 2: classification = "标准件"; break;
                case 3: classification = "参考件"; break;
                case 4: classification = PartListService.UnmatchedClassification; break;
            }

            foreach (var row in _rows)
            {
                if (classification.Length == 0 ||
                    string.Equals(row.Classification, classification, StringComparison.Ordinal))
                {
                    result.Add(row);
                }
            }
            return result;
        }

        private void ShowFilteredRows(bool markClean)
        {
            if (markClean)
            {
                foreach (var row in _rows)
                {
                    row.MarkBomClean();
                }
            }
            var filtered = FilteredRows();
            ShowRows(filtered, false);
            var filterName = _classificationFilter.SelectedIndex <= 0
                ? "全部类型"
                : Convert.ToString(_classificationFilter.SelectedItem);
            _status.Text = string.Format("{0}：显示 {1} / {2} 行。{3}",
                filterName, filtered.Count, _rows.Count, PartListService.BuildSummary(filtered));
            _exportButton.Enabled = !_running && filtered.Count > 0;
            _writeBackButton.Enabled = !_running && filtered.Count > 0 && _host.SwApp != null;
            _renameComponentsButton.Enabled = !_running && filtered.Count > 0 &&
                                              _sourceDocument.Checked && _host.SwApp != null;
        }

        private void ShowRows(IList<PartListRow> rows, bool markClean)
        {
            _grid.SuspendLayout();
            try
            {
                _grid.Rows.Clear();
                _selectAll.Checked = false;
                for (var i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (markClean)
                    {
                        row.MarkBomClean();
                    }
                    var index = _grid.Rows.Add();
                    var gridRow = _grid.Rows[index];
                    gridRow.Tag = row;
                    gridRow.Cells["select"].Value = false;
                    gridRow.Cells["sequence"].Value = i + 1;
                    gridRow.Cells["location"].Value = row.Location;
                    gridRow.Cells["fullname"].Value = row.FullName;
                    gridRow.Cells["drawing"].Value = string.IsNullOrEmpty(row.DrawingPath)
                        ? string.Empty
                        : Path.GetFileName(row.DrawingPath);
                    gridRow.Cells["classification"].Value = row.Classification;
                    gridRow.Cells["name"].Value = row.Name;
                    gridRow.Cells["material"].Value = row.Material;
                    gridRow.Cells["process"].Value = row.Process;
                    gridRow.Cells["surface"].Value = row.SurfaceTreatment;
                    gridRow.Cells["quantity"].Value = row.Quantity;
                    gridRow.Cells["assemblynote"].Value = row.AssemblyNote;
                    gridRow.Cells["remark"].Value = row.Remark;

                    // 不符合命名规则的组件排在最后并整行标红，提醒补齐命名规则。
                    if (row.IsUnmatched)
                    {
                        gridRow.DefaultCellStyle.ForeColor = UnmatchedRowColor;
                    }

                    // 有二维工程图的整行标绿；加工件没有工程图的整行标红（提醒补图）。
                    var hasDrawing = !string.IsNullOrEmpty(row.DrawingPath);
                    var missingDrawing = !hasDrawing &&
                        string.Equals(row.Classification, "加工件", StringComparison.Ordinal);
                    if (hasDrawing || missingDrawing)
                    {
                        var rowColor = hasDrawing ? DrawingRowColor : MissingDrawingRowColor;
                        for (var column = 0; column < _grid.Columns.Count; column++)
                        {
                            var columnName = _grid.Columns[column].Name;
                            if (string.Equals(columnName, "name", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(columnName, "material", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(columnName, "process", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(columnName, "remark", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(columnName, "fullname", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;   // 可编辑列保持黄色底，便于识别
                            }

                            gridRow.Cells[column].Style.BackColor = rowColor;
                        }
                    }
                }

                AutoFitGridColumnsOnce();
            }
            finally
            {
                _grid.ResumeLayout();
            }
        }

        /// <summary>
        /// 数据载入后按当前可见内容自动计算一次列宽，再切回手动模式。
        /// 用户随后可以自由拖动列边界，不会被 AutoSize 立即改回。
        /// </summary>
        private void AutoFitGridColumnsOnce()
        {
            if (_grid.Columns.Count == 0)
            {
                return;
            }

            _grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);

            for (var index = 0; index < _grid.Columns.Count; index++)
            {
                var column = _grid.Columns[index];
                int minimum;
                int maximum;
                ColumnWidthLimits(column.Name, out minimum, out maximum);
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                column.Resizable = DataGridViewTriState.True;
                column.MinimumWidth = minimum;
                column.Width = Math.Max(minimum, Math.Min(maximum, column.Width + 8));
            }

            // 表格较宽时，把剩余空间优先留给“零件名”和“备注”；较窄时保留横向滚动条。
            var used = _grid.RowHeadersVisible ? _grid.RowHeadersWidth : 0;
            foreach (DataGridViewColumn column in _grid.Columns)
            {
                used += column.Width;
            }

            var spare = _grid.ClientSize.Width - used - SystemInformation.VerticalScrollBarWidth - 4;
            if (spare > 0)
            {
                var nameExtra = Math.Min(spare, 180);
                _grid.Columns["name"].Width += nameExtra;
                spare -= nameExtra;
                if (spare > 0)
                {
                    _grid.Columns["remark"].Width += spare;
                }
            }
        }

        private static void ColumnWidthLimits(string key, out int minimum, out int maximum)
        {
            switch ((key ?? string.Empty).ToLowerInvariant())
            {
                case "sequence": minimum = 50; maximum = 70; break;
                case "location": minimum = 150; maximum = 320; break;
                case "fullname": minimum = 160; maximum = 380; break;
                case "drawing": minimum = 150; maximum = 380; break;
                case "classification": minimum = 76; maximum = 100; break;
                case "name": minimum = 150; maximum = 340; break;
                case "material":
                case "process": minimum = 105; maximum = 190; break;
                case "surface": minimum = 90; maximum = 180; break;
                case "quantity": minimum = 60; maximum = 82; break;
                case "assemblynote": minimum = 100; maximum = 240; break;
                default: minimum = 130; maximum = 300; break;
            }
        }

        /// <summary>把表格中尚未失去焦点的编辑值同步回 BOM 行对象。</summary>
        private void PullGridEdits()
        {
            _grid.EndEdit();
            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                var row = gridRow.Tag as PartListRow;
                if (row == null)
                {
                    continue;
                }
                row.Name = Convert.ToString(gridRow.Cells["name"].Value).Trim();
                row.Material = Convert.ToString(gridRow.Cells["material"].Value).Trim();
                row.Process = Convert.ToString(gridRow.Cells["process"].Value).Trim();
                row.Remark = Convert.ToString(gridRow.Cells["remark"].Value).Trim();
                row.FullName = Convert.ToString(gridRow.Cells["fullname"].Value).Trim();
            }
        }

        private void ApplyBomChanges()
        {
            if (_running || FilteredRows().Count == 0)
            {
                return;
            }

            PullGridEdits();
            var activeRows = FilteredRows();
            var changed = 0;
            foreach (var row in activeRows)
            {
                if (row.HasBomEdits)
                {
                    changed++;
                }
            }

            if (changed == 0)
            {
                MessageBox.Show(this, "BOM 表中没有需要应用的修改。", AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var message = string.Format(
                "将把 {0} 个零件的修改写入自定义属性「名称」「材料」「工艺」「备注」并保存。" +
                System.Environment.NewLine + System.Environment.NewLine +
                "本轮不会重命名零件文件，因此不会破坏装配引用。是否继续？", changed);
            if (MessageBox.Show(this, message, AddinConstants.Title,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            _running = true;
            _runButton.Enabled = false;
            _exportButton.Enabled = false;
            _writeBackButton.Enabled = false;
            _renameComponentsButton.Enabled = false;
            _status.Text = "正在应用 BOM 修改…";

            try
            {
                var updated = PartListService.ApplyBomEdits(_host.SwApp, activeRows, AppendLog);
                _status.Text = string.Format("已应用 {0} / {1} 个零件。", updated, changed);
                Log.Info(string.Format("BOM 编辑已应用：{0} / {1} 个零件。", updated, changed));

                if (updated < changed)
                {
                    MessageBox.Show(this, "部分零件未能保存，请查看窗口下方处理日志。",
                        AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                Log.Error("应用 BOM 修改失败", ex);
                MessageBox.Show(this, "应用失败：" + ex.Message, AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _running = false;
                _runButton.Enabled = true;
                ShowFilteredRows(false);
            }
        }

        private void ApplyBomChangesAndRename()
        {
            if (_running || FilteredRows().Count == 0)
            {
                return;
            }

            var active = SwUtils.ActiveDoc(_host.SwApp);
            if (active == null || active.GetType() != SwUtils.DocAssembly)
            {
                MessageBox.Show(this, "请先打开装配体，再使用“按规则重命名文件”。",
                    AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PullGridEdits();
            var activeRows = FilteredRows();
            var changed = 0;
            foreach (var row in activeRows)
            {
                if (row.HasNamingEdits)
                {
                    changed++;
                }
            }

            if (changed == 0)
            {
                MessageBox.Show(this, "BOM 表中没有需要重命名的修改。", AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var message = string.Format(
                "将按当前命名规则反推并重命名 {0} 行对应的零件文件：\r\n\r\n" +
                "1. 加工件：保留日期、变更序号和拓展代号，用材料/工艺反查材料段，用零件名更新名称段；\r\n" +
                "2. 标准件：按“工艺-零件名-材料/型号”重新组装文件名；\r\n" +
                "3. 更新当前装配体引用并保存装配体。\r\n\r\n" +
                "不会写入任何自定义属性；备注不参与文件名。是否继续？",
                changed);
            if (MessageBox.Show(this, message, AddinConstants.Title,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            _running = true;
            _runButton.Enabled = false;
            _exportButton.Enabled = false;
            _writeBackButton.Enabled = false;
            _renameComponentsButton.Enabled = false;
            _status.Text = "正在按命名规则重命名零件文件…";

            try
            {
                var renamed = PartListService.RenameAssemblyFilesByBomRules(
                    _host.SwApp, activeRows, NamingOptionsFactory.FromSettings(_host.Settings), AppendLog);

                _status.Text = string.Format("已按规则重命名 {0} 个零件文件。", renamed);
                Log.Info(string.Format("BOM 按规则重命名：{0} 个零件文件。", renamed));
                MessageBox.Show(this,
                    string.Format("处理完成。\r\n\r\n零件文件重命名：{0} 个\r\n\r\n没有写入自定义属性；当前装配体引用已同步更新。",
                        renamed),
                    AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log.Error("写入并重命名失败", ex);
                MessageBox.Show(this, "处理失败：" + ex.Message, AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _running = false;
                _runButton.Enabled = true;
                ShowFilteredRows(false);
            }
        }

        private void ExportCsv()
        {
            if (FilteredRows().Count == 0)
            {
                return;
            }

            PullGridEdits();
            var activeRows = FilteredRows();
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择明细表的保存目录";
                dialog.ShowNewFolderButton = true;
                var preferred = _host.Settings.OutputFolder;
                if (!string.IsNullOrEmpty(preferred) && Directory.Exists(preferred))
                {
                    dialog.SelectedPath = preferred;
                }

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    var title = string.IsNullOrWhiteSpace(_selectedComponentName)
                        ? "明细汇总"
                        : "部件BOM-" + SafeFileName(_selectedComponentName);
                    var path = PartListService.ExportCsv(activeRows, dialog.SelectedPath, title,
                        BuildOptions());
                    AppendLog("已导出：" + path);
                    _host.Settings.OutputFolder = dialog.SelectedPath;
                    SaveSettings();

                    if (MessageBox.Show(this, "已导出到：" + System.Environment.NewLine + path +
                            System.Environment.NewLine + System.Environment.NewLine + "是否打开所在目录？",
                            AddinConstants.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        Process.Start("explorer.exe", dialog.SelectedPath);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("导出明细失败", ex);
                    MessageBox.Show(this, "导出失败：" + ex.Message, AddinConstants.Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static string SafeFileName(string value)
        {
            var result = (value ?? string.Empty).Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(invalid, '-');
            }
            return string.IsNullOrWhiteSpace(result) ? "选中部件" : result;
        }

        private void WriteBackProperties()
        {
            if (_rows.Count == 0)
            {
                return;
            }

            var partNumberProperty = _partNumberProperty.Text.Trim();
            var materialProperty = _materialProperty.Text.Trim();
            var writePartNumber = !string.IsNullOrEmpty(partNumberProperty);
            var writeMaterial = !string.IsNullOrEmpty(materialProperty);

            if (!writePartNumber && !writeMaterial)
            {
                MessageBox.Show(this, "请填写要写回的属性名。", AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var message = string.Format(
                "将把汇总结果写回零件的自定义属性：{0}{1}。" + System.Environment.NewLine +
                System.Environment.NewLine + "这会逐个打开并保存 {2} 个零件文件，是否继续？",
                writePartNumber ? "「" + partNumberProperty + "」" : string.Empty,
                writeMaterial ? "「" + materialProperty + "」" : string.Empty,
                _rows.Count);

            if (MessageBox.Show(this, message, AddinConstants.Title,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            _running = true;
            _runButton.Enabled = false;
            _exportButton.Enabled = false;
            _writeBackButton.Enabled = false;
            _status.Text = "正在写回属性…";

            try
            {
                var updated = PartListService.WriteBack(_host.SwApp, _rows, partNumberProperty,
                    materialProperty, writePartNumber, writeMaterial, AppendLog);
                _status.Text = string.Format("已写回 {0} 个零件。", updated);
            }
            catch (Exception ex)
            {
                Log.Error("写回属性失败", ex);
                MessageBox.Show(this, "写回失败：" + ex.Message, AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _running = false;
                _runButton.Enabled = true;
                _exportButton.Enabled = true;
                _writeBackButton.Enabled = true;
            }
        }

        private void SaveSettings()
        {
            var settings = _host.Settings;
            settings.PartListOnlyMachined = _onlyMachined.Checked;
            if (settings.BomUsePropertyFields)
            {
                settings.PartListReadProperties = _readProperties.Checked;
            }
            settings.DetectVendorParts = _detectVendor.Checked;
            settings.PartNumberProperty = _partNumberProperty.Text.Trim();
            settings.MaterialProperty = _materialProperty.Text.Trim();
            settings.Save();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SaveSettings();
            // 窗口关掉后，选项卡按钮回到“用 SOLIDWORKS 里的选择”。
            _host.SetBomSelectedFiles(new List<string>());
            base.OnFormClosed(e);
        }

        private void AppendLog(string message)
        {
            if (_logBox.IsDisposed)
            {
                return;
            }

            _logBox.AppendText(message + System.Environment.NewLine);
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
            Application.DoEvents();
        }
    }
}
