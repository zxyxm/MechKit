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
        private readonly RadioButton _sourceFolder;
        private readonly TextBox _folderBox;
        private readonly CheckBox _recursive;
        private readonly ComboBox _partNumberSource;
        private readonly ComboBox _cutRule;
        private readonly TextBox _patternBox;
        private readonly ComboBox _materialSource;
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
        private readonly Button _writeBackButton;
        private readonly Button _closeButton;

        private List<PartListRow> _rows = new List<PartListRow>();
        private bool _running;

        public PartListForm(IAddinHost host)
        {
            _host = host;
            _sourceDocument = new RadioButton();
            _sourceFolder = new RadioButton();
            _folderBox = Theme.CreateTextBox();
            _recursive = new CheckBox();
            _partNumberSource = new ComboBox();
            _cutRule = new ComboBox();
            _patternBox = Theme.CreateTextBox();
            _materialSource = new ComboBox();
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
            _writeBackButton = Theme.CreatePrimaryButton("应用 BOM 修改");
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

            var subtitle = Theme.CreateLabel("双击表格可编辑名称、材料和工艺；应用后写入零件自定义属性",
                Theme.Small, Color.FromArgb(214, 232, 248));
            subtitle.Location = new Point(15, 32);

            panel.Controls.Add(title);
            panel.Controls.Add(subtitle);
            return panel;
        }

        private Control BuildSetupPanel()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 172, BackColor = Theme.Surface, Padding = new Padding(12, 8, 12, 6) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Theme.Surface
            };
            // 三行按比例分配，窗口/DPI 变化时不会把最后一行挤出可视区
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40f));

            layout.Controls.Add(BuildSourceRow(), 0, 0);
            layout.Controls.Add(BuildNamingSummaryRow(), 0, 1);
            layout.Controls.Add(BuildFilterRow(), 0, 2);

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

            _sourceDocument.Text = "当前文档";
            _sourceFolder.Text = "文件夹";
            foreach (var button in new[] { _sourceDocument, _sourceFolder })
            {
                button.Font = Theme.Body;
                button.AutoSize = true;
                button.Margin = new Padding(0, 9, 10, 0);
            }

            _folderBox.Width = 300;
            _folderBox.Margin = new Padding(0, 6, 6, 0);

            var browse = Theme.CreateSecondaryButton("浏览…");
            browse.Width = 80;
            browse.Margin = new Padding(0, 4, 10, 0);
            browse.Click += OnBrowseFolder;

            _recursive.Text = "含子文件夹";
            _recursive.Font = Theme.Body;
            _recursive.AutoSize = true;
            _recursive.Margin = new Padding(0, 9, 0, 0);

            var hint = Theme.CreateLabel("（文件夹模式汇总零件清单，不做装配体计数）", Theme.Small, Theme.Muted);
            hint.Margin = new Padding(10, 10, 0, 0);

            row.Controls.Add(_sourceDocument);
            row.Controls.Add(_sourceFolder);
            row.Controls.Add(_folderBox);
            row.Controls.Add(browse);
            row.Controls.Add(_recursive);
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
                using (var form = new NamingRuleForm(_host))
                {
                    form.ShowDialog(this);
                }

                summary.Text = DescribeRule();
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
            var separator = string.IsNullOrEmpty(s.SegmentSeparator) ? "_" : s.SegmentSeparator;
            var prefixes = s.BomPrefixes;
            var segmentOrder = NamingOptionsFactory.DescribeMachinedSegments(
                NamingOptionsFactory.ParseMachinedSegments(s.MachinedSegments));

            return string.Format(
                "当前规则：加工件 = {0}（分隔符 {1}）；标准件前缀 = {2}；只收录这两类{3}",
                segmentOrder,
                separator,
                string.IsNullOrEmpty(prefixes) ? "（未设置）" : prefixes,
                s.BomRequirePattern ? string.Empty : "（当前已关闭过滤，全部收录）");
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

            _segmentSeparator.Width = 26;
            _segmentSeparator.TextAlign = HorizontalAlignment.Center;
            _segmentSeparator.Margin = new Padding(0, 2, 4, 0);

            _materialSegment.DropDownStyle = ComboBoxStyle.DropDownList;
            _materialSegment.Font = Theme.Body;
            _materialSegment.Width = 108;
            _materialSegment.Margin = new Padding(0, 2, 14, 0);
            _materialSegment.Items.AddRange(new object[] { "材料=倒数第二段", "材料=最后一段", "材料=倒数第三段", "材料不取自名称" });

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
            Theme.StyleGrid(_grid);
            var sequenceColumn = new DataGridViewTextBoxColumn { HeaderText = "序号", Width = 50, ReadOnly = true };
            var partNumberColumn = new DataGridViewTextBoxColumn { HeaderText = "图号", Width = 140, ReadOnly = true };
            var nameColumn = new DataGridViewTextBoxColumn
            {
                HeaderText = "名称（可编辑）",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 150
            };
            var materialColumn = new DataGridViewTextBoxColumn { HeaderText = "材料（可编辑）", Width = 130 };
            var processColumn = new DataGridViewTextBoxColumn { HeaderText = "工艺（可编辑）", Width = 130 };
            var quantityColumn = new DataGridViewTextBoxColumn { HeaderText = "数量", Width = 60, ReadOnly = true };
            var typeColumn = new DataGridViewTextBoxColumn { HeaderText = "类型", Width = 70, ReadOnly = true };
            var configurationColumn = new DataGridViewTextBoxColumn { HeaderText = "配置", Width = 90, ReadOnly = true };
            var fileColumn = new DataGridViewTextBoxColumn { HeaderText = "文件名", Width = 180, ReadOnly = true };

            var editableColor = Color.FromArgb(255, 252, 226);
            nameColumn.DefaultCellStyle.BackColor = editableColor;
            materialColumn.DefaultCellStyle.BackColor = editableColor;
            processColumn.DefaultCellStyle.BackColor = editableColor;

            _grid.Columns.AddRange(sequenceColumn, partNumberColumn, nameColumn, materialColumn,
                processColumn, quantityColumn, typeColumn, configurationColumn, fileColumn);

            panel.Controls.Add(_grid);
            return panel;
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
                ColumnCount = 5,
                RowCount = 1,
                BackColor = Theme.Canvas
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));

            _status.Dock = DockStyle.Fill;
            _status.Margin = new Padding(0);
            layout.Controls.Add(_status, 0, 0);

            foreach (var button in new[] { _runButton, _exportButton, _writeBackButton, _closeButton })
            {
                button.Dock = DockStyle.Fill;
                button.Margin = new Padding(4, 8, 4, 8);
            }

            layout.Controls.Add(_runButton, 1, 0);
            layout.Controls.Add(_exportButton, 2, 0);
            layout.Controls.Add(_writeBackButton, 3, 0);
            layout.Controls.Add(_closeButton, 4, 0);

            panel.Controls.Add(layout);
            return panel;
        }

        private void WireEvents()
        {
            _runButton.Click += delegate { RunSummary(); };
            _exportButton.Click += delegate { ExportCsv(); };
            _writeBackButton.Click += delegate { ApplyBomChanges(); };
            _closeButton.Click += delegate { Close(); };

            _sourceDocument.CheckedChanged += delegate { UpdateSourceState(); };
            _cutRule.SelectedIndexChanged += delegate { UpdateSourceState(); };

            Shown += delegate
            {
                BeginInvoke((MethodInvoker)delegate { RunSummary(); });
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
            _folderBox.Text = settings.OutputFolder;
            _recursive.Checked = true;

            _partNumberSource.SelectedIndex = Clamp(settings.PartNumberSource, 0, 1);
            _cutRule.SelectedIndex = Clamp(settings.PartNumberCutRule, 0, 4);
            _patternBox.Text = settings.PartNumberPattern;
            _materialSource.SelectedIndex = Clamp(settings.MaterialSource, 0, 2);

            _onlyMachined.Checked = settings.PartListOnlyMachined;
            _excludeToolbox.Checked = true;
            _excludeSuppressed.Checked = true;
            _readProperties.Checked = settings.PartListReadProperties;
            _detectVendor.Checked = settings.DetectVendorParts;
            _useSegments.Checked = settings.UseNameSegments;
            _segmentSeparator.Text = string.IsNullOrEmpty(settings.SegmentSeparator) ? "_" : settings.SegmentSeparator;
            _materialSegment.SelectedIndex = MaterialSegmentIndexFromValue(settings.MaterialSegment);

            _partNumberProperty.Text = settings.PartNumberProperty;
            _materialProperty.Text = settings.MaterialProperty;

            _exportButton.Enabled = false;
            _writeBackButton.Enabled = false;

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
            var useFolder = _sourceFolder.Checked;
            _folderBox.Enabled = useFolder;
            _recursive.Enabled = useFolder;
            _patternBox.Enabled = _cutRule.SelectedIndex == 4;
            _patternBox.BackColor = _patternBox.Enabled ? Color.White : Color.FromArgb(242, 243, 245);
        }

        private void OnBrowseFolder(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择包含零件文件的文件夹";
                dialog.ShowNewFolderButton = false;
                if (Directory.Exists(_folderBox.Text))
                {
                    dialog.SelectedPath = _folderBox.Text;
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _folderBox.Text = dialog.SelectedPath;
                    _sourceFolder.Checked = true;
                }
            }
        }

        private PartListOptions BuildOptions()
        {
            var options = new PartListOptions
            {
                OnlyMachined = _onlyMachined.Checked,
                ExcludeToolbox = _excludeToolbox.Checked,
                DetectVendorParts = _detectVendor.Checked,
                ExcludeSuppressed = _excludeSuppressed.Checked,
                ReadCustomProperties = _readProperties.Checked
            };

            options.Naming.Source = (PartNumberSource)_partNumberSource.SelectedIndex;
            // 命名规则（来源 / 截断 / 分段 / 前缀 / 收录过滤）统一由「命名规则设置」管理
            options.Naming = NamingOptionsFactory.FromSettings(_host.Settings);
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
                MessageBox.Show(this, "未连接到 SOLIDWORKS。", AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _running = true;
            _runButton.Enabled = false;
            _exportButton.Enabled = false;
            _writeBackButton.Enabled = false;
            _logBox.Clear();
            _rows = new List<PartListRow>();
            _grid.Rows.Clear();

            try
            {
                AppendLog("规则：" + options.Naming.Describe());

                if (_sourceFolder.Checked)
                {
                    var folder = _folderBox.Text.Trim();
                    if (!Directory.Exists(folder))
                    {
                        MessageBox.Show(this, "请选择有效的文件夹。", AddinConstants.Title,
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    AppendLog(string.Format("汇总文件夹：{0}（{1}）", folder,
                        _recursive.Checked ? "含子文件夹" : "仅当前层"));
                    _rows = PartListService.FromFolder(swApp, folder, _recursive.Checked, options, AppendLog);
                }
                else
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

                        AppendLog("统计装配体：" + Path.GetFileName(doc.GetPathName()));
                        _rows = PartListService.FromAssembly(swApp, assembly, options, AppendLog);
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

                ShowRows(_rows);
                var summary = PartListService.BuildSummary(_rows);
                AppendLog(summary);
                _status.Text = summary;
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
                _exportButton.Enabled = _rows.Count > 0;
                _writeBackButton.Enabled = _rows.Count > 0;
            }
        }

        private void ShowRows(IList<PartListRow> rows)
        {
            _grid.SuspendLayout();
            try
            {
                _grid.Rows.Clear();
                for (var i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    row.MarkBomClean();
                    var index = _grid.Rows.Add();
                    var gridRow = _grid.Rows[index];
                    gridRow.Cells[0].Value = i + 1;
                    gridRow.Cells[1].Value = row.PartNumber;
                    gridRow.Cells[2].Value = row.Name;
                    gridRow.Cells[3].Value = row.Material;
                    gridRow.Cells[4].Value = row.Process;
                    gridRow.Cells[5].Value = row.Quantity;
                    gridRow.Cells[6].Value = row.Classification;
                    gridRow.Cells[7].Value = row.Configuration;
                    gridRow.Cells[8].Value = row.FileName;
                }
            }
            finally
            {
                _grid.ResumeLayout();
            }
        }

        /// <summary>把表格中尚未失去焦点的编辑值同步回 BOM 行对象。</summary>
        private void PullGridEdits()
        {
            _grid.EndEdit();
            var count = Math.Min(_rows.Count, _grid.Rows.Count);
            for (var i = 0; i < count; i++)
            {
                var gridRow = _grid.Rows[i];
                _rows[i].Name = Convert.ToString(gridRow.Cells[2].Value).Trim();
                _rows[i].Material = Convert.ToString(gridRow.Cells[3].Value).Trim();
                _rows[i].Process = Convert.ToString(gridRow.Cells[4].Value).Trim();
            }
        }

        private void ApplyBomChanges()
        {
            if (_running || _rows.Count == 0)
            {
                return;
            }

            PullGridEdits();
            var changed = 0;
            foreach (var row in _rows)
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
                "将把 {0} 个零件的修改写入自定义属性「名称」「材料」「工艺」并保存。" +
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
            _status.Text = "正在应用 BOM 修改…";

            try
            {
                var updated = PartListService.ApplyBomEdits(_host.SwApp, _rows, AppendLog);
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
                _exportButton.Enabled = _rows.Count > 0;
                _writeBackButton.Enabled = _rows.Count > 0;
            }
        }

        private void ExportCsv()
        {
            if (_rows.Count == 0)
            {
                return;
            }

            PullGridEdits();
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
                    var path = PartListService.ExportCsv(_rows, dialog.SelectedPath, "明细汇总");
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
            settings.PartListReadProperties = _readProperties.Checked;
            settings.DetectVendorParts = _detectVendor.Checked;
            settings.PartNumberProperty = _partNumberProperty.Text.Trim();
            settings.MaterialProperty = _materialProperty.Text.Trim();
            settings.Save();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SaveSettings();
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
