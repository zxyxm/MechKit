using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MechKit.Core;
using MechKit.Features;

namespace MechKit.UI
{
    /// <summary>统一设置：BOM 格式 + 个人配置与迁移。</summary>
    internal sealed class SettingsForm : Form
    {
        private readonly IAddinHost _host;
        private readonly TextBox _weldment;
        private readonly TextBox _template;
        private readonly TextBox _macro;
        private readonly TextBox _toolbox;
        private readonly TextBox _prefixes;
        private readonly CheckBox _requirePattern;
        private readonly CheckBox _usePropertyFields;
        private readonly ComboBox _assemblyLevel;
        private readonly ComboBox _standardLocationField;
        private readonly ComboBox _machinedLocationField;
        private readonly TextBox _sequenceHeader;
        private readonly TextBox _locationHeader;
        private readonly TextBox _fullNameHeader;
        private readonly TextBox _drawingHeader;
        private readonly TextBox _classificationHeader;
        private readonly TextBox _nameHeader;
        private readonly TextBox _materialHeader;
        private readonly TextBox _processHeader;
        private readonly TextBox _surfaceHeader;
        private readonly TextBox _quantityHeader;
        private readonly TextBox _assemblyNoteHeader;
        private readonly TextBox _remarkHeader;
        private readonly ComboBox _standardAssemblyNoteField;
        private readonly ComboBox _machinedAssemblyNoteField;
        private readonly ComboBox _standardNameField;
        private readonly ComboBox _standardMaterialField;
        private readonly ComboBox _standardProcessField;
        private readonly ComboBox _standardRemarkField;
        private readonly ComboBox _machinedNameField;
        private readonly ComboBox _machinedMaterialField;
        private readonly ComboBox _machinedProcessField;
        private readonly ComboBox _standardSurfaceField;
        private readonly ComboBox _machinedSurfaceField;
        private readonly ComboBox _machinedRemarkField;
        private readonly ComboBox _columnOrderSelector;
        private readonly Label _status;
        private TableLayoutPanel _mappingTable;
        private List<string> _columnOrder = new List<string>();
        private bool _syncingAssemblyLevel;
        private readonly int _initialTab;

        public SettingsForm(IAddinHost host)
            : this(host, 0)
        {
        }

        /// <summary>initialTab：0 = BOM 格式，1 = 个人配置。</summary>
        public SettingsForm(IAddinHost host, int initialTab)
        {
            _host = host;
            _initialTab = initialTab;
            _weldment = Theme.CreateTextBox();
            _template = Theme.CreateTextBox();
            _macro = Theme.CreateTextBox();
            _toolbox = Theme.CreateTextBox();
            _prefixes = Theme.CreateTextBox();
            _requirePattern = new CheckBox();
            _usePropertyFields = new CheckBox();
            _assemblyLevel = CreateAssemblyLevelCombo(false);
            _standardLocationField = CreateAssemblyLevelCombo(true);
            _machinedLocationField = CreateAssemblyLevelCombo(true);
            _sequenceHeader = Theme.CreateTextBox();
            _locationHeader = Theme.CreateTextBox();
            _fullNameHeader = Theme.CreateTextBox();
            _drawingHeader = Theme.CreateTextBox();
            _classificationHeader = Theme.CreateTextBox();
            _nameHeader = Theme.CreateTextBox();
            _materialHeader = Theme.CreateTextBox();
            _processHeader = Theme.CreateTextBox();
            _surfaceHeader = Theme.CreateTextBox();
            _quantityHeader = Theme.CreateTextBox();
            _assemblyNoteHeader = Theme.CreateTextBox();
            _remarkHeader = Theme.CreateTextBox();
            _standardAssemblyNoteField = CreateFieldSourceCombo(true);
            _machinedAssemblyNoteField = CreateFieldSourceCombo(false);
            _standardNameField = CreateFieldSourceCombo(true);
            _standardMaterialField = CreateFieldSourceCombo(true);
            _standardProcessField = CreateFieldSourceCombo(true);
            _standardRemarkField = CreateFieldSourceCombo(true);
            _machinedNameField = CreateFieldSourceCombo(false);
            _machinedMaterialField = CreateFieldSourceCombo(false);
            _machinedProcessField = CreateFieldSourceCombo(false);
            _standardSurfaceField = CreateFieldSourceCombo(true);
            _machinedSurfaceField = CreateFieldSourceCombo(false);
            _machinedRemarkField = CreateFieldSourceCombo(false);
            _columnOrderSelector = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = Theme.Small,
                Width = 190
            };
            _status = Theme.CreateValueLabel("就绪");

            BuildLayout();
            WireAssemblyLevelSync();
            LoadFromSettings();
            _usePropertyFields.CheckedChanged += delegate { RebuildFieldSourceCombos(); };
        }

        private void BuildLayout()
        {
            Text = "MechKit 设置";
            Font = Theme.Body;
            BackColor = Theme.Canvas;
            StartPosition = FormStartPosition.CenterParent;
            WindowLayout.Attach(this, _host.Settings, new Size(1180, 700), new Size(1040, 600));

            var header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Theme.Accent };
            var title = Theme.CreateLabel("设置", Theme.Title, Color.White);
            title.Location = new Point(14, 9);
            var subtitle = Theme.CreateLabel("BOM 格式 · 装配层级 · 个人配置与设置迁移",
                Theme.Small, Color.FromArgb(214, 232, 248));
            subtitle.Location = new Point(15, 32);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 6), BackColor = Theme.Canvas };

            var tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = Theme.Body
            };

            var bomTab = new TabPage("BOM 格式") { BackColor = Theme.Canvas, Padding = new Padding(8) };
            bomTab.Controls.Add(BuildBomPanel());

            var personalTab = new TabPage("个人配置") { BackColor = Theme.Canvas, Padding = new Padding(8) };
            var personal = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Theme.Canvas
            };
            personal.RowStyles.Add(new RowStyle(SizeType.Absolute, 214f));
            personal.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            personal.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            personal.Controls.Add(BuildFolderPanel(), 0, 0);
            personal.Controls.Add(BuildTransferPanel(), 0, 1);
            personal.Controls.Add(BuildStatusBar(), 0, 2);
            personalTab.Controls.Add(personal);

            tabs.TabPages.Add(bomTab);
            tabs.TabPages.Add(personalTab);
            tabs.SelectedIndex = Math.Max(0, Math.Min(tabs.TabPages.Count - 1, _initialTab));
            body.Controls.Add(tabs);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.Canvas, Padding = new Padding(14, 6, 14, 12) };
            var close = Theme.CreatePrimaryButton("关闭");
            close.Width = 96;
            close.Dock = DockStyle.Right;
            close.Click += delegate { Close(); };
            footer.Controls.Add(close);

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);
        }

        private Control BuildBomPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Surface,
                Padding = new Padding(18, 16, 18, 16)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6,
                BackColor = Theme.Surface
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));

            var heading = Theme.CreateLabel("BOM 表格格式", Theme.BodyBold, Theme.Text);
            heading.Dock = DockStyle.Fill;
            layout.Controls.Add(heading, 0, 0);
            layout.SetColumnSpan(heading, 2);

            layout.Controls.Add(BomLabel("位置显示层级"), 0, 1);
            var levelEditor = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Surface,
                Margin = new Padding(0)
            };
            _assemblyLevel.Width = 360;
            _assemblyLevel.Margin = new Padding(0, 5, 12, 5);
            var levelHint = Theme.CreateValueLabel("与下方标准件、加工件的“位置”字段同步");
            levelHint.AutoSize = true;
            levelHint.Font = Theme.Small;
            levelHint.ForeColor = Theme.Muted;
            levelHint.Margin = new Padding(0, 10, 0, 0);
            levelEditor.Controls.Add(_assemblyLevel);
            levelEditor.Controls.Add(levelHint);
            layout.Controls.Add(levelEditor, 1, 1);

            _requirePattern.Text = "只收录加工件与标准件（“参考-”开头的参考件不进入 BOM）";
            _requirePattern.AutoSize = true;
            _requirePattern.ForeColor = Theme.Text;
            _requirePattern.Margin = new Padding(0, 8, 0, 0);
            layout.Controls.Add(BomLabel("收录范围"), 0, 2);
            layout.Controls.Add(_requirePattern, 1, 2);

            _usePropertyFields.Text = "使用 SOLIDWORKS 属性表字段";
            _usePropertyFields.AutoSize = true;
            _usePropertyFields.ForeColor = Theme.Text;
            _usePropertyFields.Margin = new Padding(0, 7, 12, 0);
            var propertySource = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Surface,
                Margin = new Padding(0)
            };
            propertySource.Controls.Add(_usePropertyFields);
            var propertyHint = Theme.CreateLabel(
                "加工[2]展开为：[2][1]材料、[2][2]工艺、[2][3]表面处理；勾选后追加属性表字段。",
                Theme.Small, Theme.Muted);
            propertyHint.Margin = new Padding(0, 9, 0, 0);
            propertySource.Controls.Add(propertyHint);
            layout.Controls.Add(BomLabel("字段来源"), 0, 3);
            layout.Controls.Add(propertySource, 1, 3);

            var mapping = BuildFieldMappingTable();
            layout.Controls.Add(BomLabel("表头字段映射"), 0, 4);
            layout.Controls.Add(mapping, 1, 4);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Surface
            };
            var save = Theme.CreatePrimaryButton("保存 BOM 设置");
            save.Width = 130;
            save.Click += delegate { SaveSettings(); };
            var naming = Theme.CreateSecondaryButton("命名规则设置…");
            naming.Width = 140;
            naming.Margin = new Padding(8, 0, 0, 0);
            naming.Click += delegate
            {
                _host.ShowNamingRuleDialog(0, LoadFromSettings);
            };
            actions.Controls.Add(save);
            actions.Controls.Add(naming);
            var moveLabel = Theme.CreateLabel("移动表头", Theme.Small, Theme.Muted);
            moveLabel.AutoSize = true;
            moveLabel.Margin = new Padding(22, 7, 6, 0);
            _columnOrderSelector.Margin = new Padding(0, 2, 6, 0);
            var moveLeft = Theme.CreateSecondaryButton("← 左移");
            moveLeft.Width = 72;
            moveLeft.Margin = new Padding(0, 0, 4, 0);
            moveLeft.Click += delegate { MoveSelectedColumn(-1); };
            var moveRight = Theme.CreateSecondaryButton("右移 →");
            moveRight.Width = 72;
            moveRight.Margin = new Padding(0);
            moveRight.Click += delegate { MoveSelectedColumn(1); };
            actions.Controls.Add(moveLabel);
            actions.Controls.Add(_columnOrderSelector);
            actions.Controls.Add(moveLeft);
            actions.Controls.Add(moveRight);
            layout.Controls.Add(actions, 1, 5);

            panel.Controls.Add(layout);
            return panel;
        }

        private Control BuildFieldMappingTable()
        {
            var scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(248, 249, 250),
                Padding = new Padding(0, 0, 0, 4)
            };
            var table = new TableLayoutPanel
            {
                Location = new Point(0, 0),
                Size = new Size(1870, 158),
                ColumnCount = 12,
                RowCount = 3,
                BackColor = Color.FromArgb(248, 249, 250),
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
                Padding = new Padding(0)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72f));
            var widths = new[] { 90f, 130f, 210f, 90f, 230f, 170f, 210f, 190f, 90f, 200f, 170f };
            for (var i = 0; i < widths.Length; i++)
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, widths[i]));
            }
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));

            _mappingTable = table;
            if (_columnOrder.Count == 0)
            {
                _columnOrder.AddRange(PartListService.ParseColumnOrder(_host.Settings.BomColumnOrder));
            }
            RebuildMappingTable();
            scrollHost.Controls.Add(table);
            return scrollHost;
        }

        private void RebuildMappingTable()
        {
            if (_mappingTable == null)
            {
                return;
            }

            _mappingTable.SuspendLayout();
            try
            {
                _mappingTable.Controls.Clear();
                var corner = Theme.CreateLabel(string.Empty, Theme.Small, Theme.Text);
                corner.Dock = DockStyle.Fill;
                _mappingTable.Controls.Add(corner, 0, 0);

                var standard = new List<Control>();
                var machined = new List<Control>();
                for (var column = 0; column < _columnOrder.Count; column++)
                {
                    var key = _columnOrder[column];
                    var editor = HeaderEditor(key);
                    editor.Dock = DockStyle.Fill;
                    editor.Margin = new Padding(2, 5, 2, 5);
                    editor.TextAlign = HorizontalAlignment.Center;
                    editor.Font = Theme.Small;
                    _mappingTable.Controls.Add(editor, column + 1, 0);
                    standard.Add(MappingControl(key, true));
                    machined.Add(MappingControl(key, false));
                }

                AddMappingRow(_mappingTable, 1, "标准件", standard.ToArray());
                AddMappingRow(_mappingTable, 2, "加工件", machined.ToArray());
            }
            finally
            {
                _mappingTable.ResumeLayout();
            }
        }

        private TextBox HeaderEditor(string key)
        {
            switch (key)
            {
                case "sequence": return _sequenceHeader;
                case "location": return _locationHeader;
                case "fullname": return _fullNameHeader;
                case "drawing": return _drawingHeader;
                case "classification": return _classificationHeader;
                case "name": return _nameHeader;
                case "material": return _materialHeader;
                case "process": return _processHeader;
                case "surface": return _surfaceHeader;
                case "quantity": return _quantityHeader;
                case "assemblynote": return _assemblyNoteHeader;
                default: return _remarkHeader;
            }
        }

        private Control MappingControl(string key, bool standard)
        {
            switch (key)
            {
                case "sequence": return BuildFixedCombo("自动序号");
                case "location": return standard ? _standardLocationField : _machinedLocationField;
                case "fullname": return BuildFixedCombo("完整名称");
                case "drawing": return BuildFixedCombo("自动（同名工程图）");
                case "classification": return BuildFixedCombo(standard ? "标准件" : "加工件");
                case "name": return standard ? _standardNameField : _machinedNameField;
                case "material": return standard ? _standardMaterialField : _machinedMaterialField;
                case "process": return standard ? _standardProcessField : _machinedProcessField;
                case "surface": return standard ? _standardSurfaceField : _machinedSurfaceField;
                case "quantity": return BuildFixedCombo("统计数量");
                case "assemblynote": return standard ? _standardAssemblyNoteField : _machinedAssemblyNoteField;
                default: return standard ? _standardRemarkField : _machinedRemarkField;
            }
        }

        private void RefreshColumnOrderSelector(string selectedKey)
        {
            _columnOrderSelector.Items.Clear();
            foreach (var key in _columnOrder)
            {
                _columnOrderSelector.Items.Add(new ColumnOrderItem(
                    key, HeaderEditor(key).Text));
            }

            var selectedIndex = 0;
            for (var index = 0; index < _columnOrderSelector.Items.Count; index++)
            {
                var item = _columnOrderSelector.Items[index] as ColumnOrderItem;
                if (item != null && string.Equals(item.Key, selectedKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = index;
                    break;
                }
            }
            _columnOrderSelector.SelectedIndex = _columnOrderSelector.Items.Count == 0
                ? -1 : selectedIndex;
        }

        private void MoveSelectedColumn(int offset)
        {
            var item = _columnOrderSelector.SelectedItem as ColumnOrderItem;
            if (item == null)
            {
                return;
            }

            var index = _columnOrder.IndexOf(item.Key);
            var target = index + offset;
            if (index < 0 || target < 0 || target >= _columnOrder.Count)
            {
                return;
            }

            _columnOrder[index] = _columnOrder[target];
            _columnOrder[target] = item.Key;
            RebuildMappingTable();
            RefreshColumnOrderSelector(item.Key);
        }

        private sealed class ColumnOrderItem
        {
            public ColumnOrderItem(string key, string text)
            {
                Key = key;
                Text = string.IsNullOrWhiteSpace(text) ? key : text.Trim();
            }

            public string Key { get; private set; }

            public string Text { get; private set; }

            public override string ToString()
            {
                return Text;
            }
        }

        private static void AddMappingRow(TableLayoutPanel table, int row, string title,
            params Control[] controls)
        {
            var label = Theme.CreateLabel(title, Theme.BodyBold, Theme.Text);
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleCenter;
            table.Controls.Add(label, 0, row);
            for (var i = 0; i < controls.Length; i++)
            {
                controls[i].Dock = DockStyle.Fill;
                controls[i].Margin = new Padding(2, 10, 2, 10);
                table.Controls.Add(controls[i], i + 1, row);
            }
        }

        private static ComboBox BuildFixedCombo(string text)
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = Theme.Small
            };
            combo.Items.Add(text);
            combo.SelectedIndex = 0;
            return combo;
        }

        private static ComboBox CreateAssemblyLevelCombo(bool compact)
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = compact ? Theme.Small : Theme.Body
            };
            combo.Items.AddRange(compact
                ? new object[] { "一级·总装", "二级·部装", "三级·完整" }
                : new object[]
                {
                    "一级：总装（最大的装配体）",
                    "二级：总装 > 部装（含子装配体）",
                    "三级：总装 > 部装 > 零件/小装配体（完整路径）"
                });
            return combo;
        }

        /// <summary>顶部层级与两类 BOM 的“位置”字段双向同步。</summary>
        private void WireAssemblyLevelSync()
        {
            _assemblyLevel.SelectedIndexChanged += delegate { SyncAssemblyLevel(_assemblyLevel); };
            _standardLocationField.SelectedIndexChanged += delegate { SyncAssemblyLevel(_standardLocationField); };
            _machinedLocationField.SelectedIndexChanged += delegate { SyncAssemblyLevel(_machinedLocationField); };
        }

        private void SyncAssemblyLevel(ComboBox source)
        {
            if (_syncingAssemblyLevel || source == null || source.SelectedIndex < 0)
            {
                return;
            }

            _syncingAssemblyLevel = true;
            try
            {
                var index = source.SelectedIndex;
                _assemblyLevel.SelectedIndex = index;
                _standardLocationField.SelectedIndex = index;
                _machinedLocationField.SelectedIndex = index;
            }
            finally
            {
                _syncingAssemblyLevel = false;
            }
        }

        private ComboBox CreateFieldSourceCombo(bool standard)
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = Theme.Small,
                DropDownWidth = 290
            };
            combo.Tag = standard;
            PopulateFieldSourceCombo(combo, standard, _host.Settings.BomUsePropertyFields);
            return combo;
        }

        private void PopulateFieldSourceCombo(ComboBox combo, bool standard, bool includeProperties)
        {
            if (combo == null)
            {
                return;
            }

            combo.Items.Clear();
            if (standard)
            {
                AddFieldSource(combo, "auto", "自动·标准件规则（前缀-中间名-型号）");
                AddFieldSource(combo, "whole", "完整名称");
                AddFieldSource(combo, "segment:1", "标准[1] 前缀");
                AddFieldSource(combo, "segment:2", "标准[2] 中间名");
                AddFieldSource(combo, "tail:3", "标准[3] 型号（完整）");
            }
            else
            {
                AddFieldSource(combo, "auto", "自动·加工件规则（优先命名规则）");
                AddFieldSource(combo, "whole", "完整名称");

                var settings = _host.Settings;
                var segments = NamingOptionsFactory.ParseMachinedSegments(settings.MachinedSegments);
                var labels = NamingOptionsFactory.ParseMachinedSegmentLabels(
                    settings.MachinedSegmentLabels, segments.Length);
                var flags = NamingOptionsFactory.ParseMachinedSegmentBomNameFlags(
                    settings.MachinedSegmentBomNameFlags, segments);
                for (var index = 0; index < segments.Length; index++)
                {
                    var label = MachinedSegmentDisplayName(segments[index], labels[index]);
                    if (index < flags.Length && flags[index])
                    {
                        label += " · 已并入 BOM";
                    }
                    if (segments[index] == MachinedSegmentKind.Material)
                    {
                        AddFieldSource(combo, "machined2:material",
                            string.Format("加工[{0}][1] 材料", index + 1));
                        AddFieldSource(combo, "machined2:process",
                            string.Format("加工[{0}][2] 工艺", index + 1));
                        AddFieldSource(combo, "machined2:surface",
                            string.Format("加工[{0}][3] 表面处理", index + 1));
                    }
                    else
                    {
                        AddFieldSource(combo,
                            NamingOptionsFactory.MachinedSegmentFieldCode(segments[index], labels[index]),
                            string.Format("加工[{0}] {1}", index + 1, label));
                    }
                }
            }

            AddFieldSource(combo, "empty", "留空");
            if (includeProperties)
            {
                AddFieldSource(combo, "property:name", "属性表·名称");
                AddFieldSource(combo, "property:material", "属性表·材料");
                AddFieldSource(combo, "property:process", "属性表·工艺");
                AddFieldSource(combo, "property:remark", "属性表·备注");
                AddFieldSource(combo, "property:surface", "属性表·表面处理");
                AddFieldSource(combo, "property:assemblynote", "属性表·装配说明");
            }
        }

        private static void AddFieldSource(ComboBox combo, string code, string text)
        {
            combo.Items.Add(new FieldSourceItem(code, text));
        }

        private sealed class FieldSourceItem
        {
            public FieldSourceItem(string code, string text)
            {
                Code = code;
                Text = text;
            }

            public string Code { get; private set; }

            public string Text { get; private set; }

            public override string ToString()
            {
                return Text;
            }
        }

        private static string MachinedSegmentDisplayName(MachinedSegmentKind kind, string customLabel)
        {
            switch (kind)
            {
                case MachinedSegmentKind.Date: return "时间";
                case MachinedSegmentKind.Material: return "材料/工艺";
                case MachinedSegmentKind.Name: return "零件名称";
                case MachinedSegmentKind.Serial: return "版本号";
                case MachinedSegmentKind.Extension: return "拓展代号";
                default:
                    return string.IsNullOrWhiteSpace(customLabel) ? "自定义" : customLabel.Trim();
            }
        }

        private static Label BomLabel(string text)
        {
            var label = Theme.CreateFieldLabel(text);
            label.Dock = DockStyle.Fill;
            label.ForeColor = Theme.Text;
            return label;
        }

        private Control BuildFolderPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(12, 10, 12, 10), AutoScroll = true };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                BackColor = Theme.Surface
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
            for (var i = 0; i < 4; i++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            }
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));

            layout.Controls.Add(Theme.CreateLabel("常用目录（点「打开」直接在资源管理器里打开）", Theme.BodyBold, Theme.Text), 0, 0);
            layout.Controls.Add(BuildFolderRow("焊件库", _weldment, () => SwFolders.WeldmentProfiles()), 0, 1);
            layout.Controls.Add(BuildFolderRow("工程图/零件模板", _template, () => SwFolders.DrawingTemplates()), 0, 2);
            layout.Controls.Add(BuildFolderRow("宏", _macro, () => SwFolders.Macros()), 0, 3);
            layout.Controls.Add(BuildFolderRow("Toolbox", _toolbox, () => SwFolders.Toolbox()), 0, 4);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Surface
            };

            var detect = Theme.CreateSecondaryButton("自动检测");
            detect.Width = 96;
            detect.Margin = new Padding(0, 2, 8, 0);
            detect.Click += delegate { DetectAll(); };

            var save = Theme.CreatePrimaryButton("保存个人配置");
            save.Width = 120;
            save.Margin = new Padding(0, 2, 0, 0);
            save.Click += delegate { SaveSettings(); };

            var weldment = Theme.CreatePrimaryButton("一键迁移焊件库到 SW");
            weldment.Width = 170;
            weldment.Margin = new Padding(12, 2, 0, 0);
            weldment.Click += delegate { InstallWeldmentLibrary(); };

            var naming = Theme.CreateSecondaryButton("命名规则设置…");
            naming.Width = 140;
            naming.Margin = new Padding(8, 2, 0, 0);
            naming.Click += delegate
            {
                _host.ShowNamingRuleDialog(0, LoadFromSettings);
            };

            buttons.Controls.Add(detect);
            buttons.Controls.Add(save);
            buttons.Controls.Add(weldment);
            buttons.Controls.Add(naming);
            layout.Controls.Add(buttons, 0, 5);

            panel.Controls.Add(layout);
            return panel;
        }

        /// <summary>BOM 命名前缀与收录规则。</summary>
        private Control BuildPrefixRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Theme.Surface
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250f));

            var label = Theme.CreateFieldLabel("标准件前缀");
            label.Dock = DockStyle.Fill;
            label.ForeColor = Theme.Text;

            _prefixes.Dock = DockStyle.Fill;
            _prefixes.Margin = new Padding(0, 4, 6, 4);

            _requirePattern.Text = "只收录加工件与标准件（“参考-”开头的参考件不进入 BOM）";
            _requirePattern.AutoSize = true;
            _requirePattern.ForeColor = Theme.Text;
            _requirePattern.Margin = new Padding(0, 8, 0, 0);

            row.Controls.Add(label, 0, 0);
            row.Controls.Add(_prefixes, 1, 0);
            row.Controls.Add(_requirePattern, 2, 0);
            return row;
        }

        private Control BuildFolderRow(string caption, TextBox box, Func<string> detector)
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = Theme.Surface
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62f));

            var label = Theme.CreateFieldLabel(caption);
            label.Dock = DockStyle.Fill;
            label.ForeColor = Theme.Text;

            box.Dock = DockStyle.Fill;
            box.Margin = new Padding(0, 4, 6, 4);

            var browse = Theme.CreateSecondaryButton("浏览…");
            browse.Dock = DockStyle.Fill;
            browse.Margin = new Padding(0, 2, 6, 2);
            browse.Click += delegate
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "选择" + caption + "目录";
                    if (Directory.Exists(box.Text))
                    {
                        dialog.SelectedPath = box.Text;
                    }

                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        box.Text = dialog.SelectedPath;
                    }
                }
            };

            var open = Theme.CreateSecondaryButton("打开");
            open.Dock = DockStyle.Fill;
            open.Margin = new Padding(0, 2, 0, 2);
            open.Click += delegate
            {
                var path = box.Text;
                if (string.IsNullOrEmpty(path))
                {
                    path = detector();
                    box.Text = path;
                }

                OpenFolder(path);
            };

            row.Controls.Add(label, 0, 0);
            row.Controls.Add(box, 1, 0);
            row.Controls.Add(browse, 2, 0);
            row.Controls.Add(open, 3, 0);
            return row;
        }

        private Control BuildTransferPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Surface,
                Padding = new Padding(12, 10, 12, 10),
                Margin = new Padding(0, 8, 0, 0),
                AutoScroll = true
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 8,
                BackColor = Theme.Surface
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));

            layout.Controls.Add(Theme.CreateLabel("MechKit 配置与预设迁移", Theme.BodyBold, Theme.Text), 0, 0);

            var portableHint = Theme.CreateLabel(
                "导出命名规则、前缀/中间名、材料工艺预设与 BOM 设置。换电脑后将文件复制到 MechKit.dll 同目录（通常 C:\\MechKit），重启 SOLIDWORKS 自动生效。",
                Theme.Small, Theme.Muted);
            portableHint.Dock = DockStyle.Fill;
            layout.Controls.Add(portableHint, 0, 1);

            var portableActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Surface,
                Margin = new Padding(0)
            };
            var portableExport = Theme.CreatePrimaryButton("导出 MechKit 配置…");
            portableExport.Width = 170;
            portableExport.Click += delegate { ExportMechKitConfiguration(); };
            var openInstall = Theme.CreateSecondaryButton("打开安装目录");
            openInstall.Width = 130;
            openInstall.Margin = new Padding(10, 0, 0, 0);
            openInstall.Click += delegate { OpenFolder(Path.GetDirectoryName(AddinSettings.PortableSettingsPath)); };
            portableActions.Controls.Add(portableExport);
            portableActions.Controls.Add(openInstall);
            layout.Controls.Add(portableActions, 0, 2);

            layout.Controls.Add(Theme.CreateLabel("SOLIDWORKS 个人设置迁移", Theme.BodyBold, Theme.Text), 0, 3);

            var exportHint = Theme.CreateLabel(
                "导出：把当前 SOLIDWORKS 的个人设置保存成一个文件（界面布局、笔势、快捷键、文件位置、导出选项等）",
                Theme.Small, Theme.Muted);
            exportHint.Dock = DockStyle.Fill;
            layout.Controls.Add(exportHint, 0, 4);

            var exportButton = Theme.CreatePrimaryButton("导出我的设置…");
            exportButton.Width = 150;
            exportButton.Click += delegate { Export(); };

            var saveConfigButton = Theme.CreateSecondaryButton("保存配置");
            saveConfigButton.Width = 130;
            saveConfigButton.Margin = new Padding(10, 0, 0, 0);
            saveConfigButton.Click += delegate
            {
                SaveSettings();
                _status.Text = "当前 MechKit 配置已保存。";
            };

            var exportActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Surface,
                Margin = new Padding(0)
            };
            exportActions.Controls.Add(exportButton);
            exportActions.Controls.Add(saveConfigButton);
            layout.Controls.Add(exportActions, 0, 5);

            var importHint = Theme.CreateLabel(
                "导入：在另一台电脑 / 另一个 SOLIDWORKS 上导入该文件，导入后重启 SOLIDWORKS 生效",
                Theme.Small, Theme.Muted);
            importHint.Dock = DockStyle.Fill;
            layout.Controls.Add(importHint, 0, 6);

            var importButton = Theme.CreateSecondaryButton("导入设置…");
            importButton.Width = 150;
            importButton.Dock = DockStyle.Left;
            importButton.Click += delegate { Import(); };
            layout.Controls.Add(importButton, 0, 7);

            panel.Controls.Add(layout);
            return panel;
        }

        private void ExportMechKitConfiguration()
        {
            SaveSettings();

            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "导出 MechKit 配置与预设";
                dialog.Filter = "MechKit 便携配置 (*.ini)|*.ini|所有文件 (*.*)|*.*";
                dialog.FileName = AddinSettings.PortableFileName;
                dialog.DefaultExt = "ini";
                dialog.AddExtension = true;

                var folder = _host.Settings.SettingsBackupFolder;
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    dialog.InitialDirectory = folder;
                }

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                string message;
                var ok = _host.Settings.ExportPortable(dialog.FileName, out message);
                if (ok)
                {
                    _host.Settings.SettingsBackupFolder = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
                    _host.Settings.Save();
                    message += Environment.NewLine + Environment.NewLine +
                               "换电脑使用：把文件放到 " +
                               Path.GetDirectoryName(AddinSettings.PortableSettingsPath) +
                               "，并保持文件名为 " + AddinSettings.PortableFileName + "。";
                }

                _status.Text = ok ? "MechKit 便携配置已导出。" : message;
                Log.Info(message);
                MessageBox.Show(this, message, "MechKit",
                    MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
        }

        private Control BuildStatusBar()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(238, 241, 244) };
            _status.Dock = DockStyle.Fill;
            _status.Font = Theme.Small;
            _status.ForeColor = Theme.Muted;
            _status.Padding = new Padding(10, 0, 6, 0);
            panel.Controls.Add(_status);
            return panel;
        }

        private void LoadFromSettings()
        {
            var settings = _host.Settings;
            _weldment.Text = string.IsNullOrEmpty(settings.WeldmentFolder) ? SwFolders.WeldmentProfiles() : settings.WeldmentFolder;
            _template.Text = string.IsNullOrEmpty(settings.TemplateFolder) ? SwFolders.DrawingTemplates() : settings.TemplateFolder;
            _macro.Text = string.IsNullOrEmpty(settings.MacroFolder) ? SwFolders.Macros() : settings.MacroFolder;
            _toolbox.Text = string.IsNullOrEmpty(settings.ToolboxFolder) ? SwFolders.Toolbox() : settings.ToolboxFolder;
            _prefixes.Text = settings.BomPrefixes;
            _requirePattern.Checked = settings.BomRequirePattern;
            _usePropertyFields.Checked = settings.BomUsePropertyFields;
            RebuildFieldSourceCombos();
            _assemblyLevel.SelectedIndex = AssemblyLevelToIndex(settings.BomAssemblyLevel);
            SyncAssemblyLevel(_assemblyLevel);
            _sequenceHeader.Text = HeaderOrDefault(settings.BomSequenceHeader, "序号");
            _locationHeader.Text = HeaderOrDefault(settings.BomLocationHeader, "位置");
            _fullNameHeader.Text = HeaderOrDefault(settings.BomFullNameHeader, "完整名称");
            _drawingHeader.Text = HeaderOrDefault(settings.BomDrawingHeader, "二维工程图");
            _classificationHeader.Text = HeaderOrDefault(settings.BomClassificationHeader, "属性");
            _nameHeader.Text = HeaderOrDefault(settings.BomNameHeader, "零件名称/标准件名称");
            _materialHeader.Text = HeaderOrDefault(settings.BomMaterialHeader, "材料/型号");
            _processHeader.Text = HeaderOrDefault(settings.BomProcessHeader, "工艺/渠道");
            _surfaceHeader.Text = HeaderOrDefault(settings.BomSurfaceHeader, "表面处理");
            _quantityHeader.Text = HeaderOrDefault(settings.BomQuantityHeader, "数量");
            _assemblyNoteHeader.Text = HeaderOrDefault(settings.BomAssemblyNoteHeader, "安装说明");
            _remarkHeader.Text = HeaderOrDefault(settings.BomRemarkHeader, "备注");
            SelectFieldSource(_standardAssemblyNoteField, settings.BomStandardAssemblyNoteField);
            SelectFieldSource(_machinedAssemblyNoteField, settings.BomMachinedAssemblyNoteField);
            SelectFieldSource(_standardSurfaceField, settings.BomStandardSurfaceField);
            SelectFieldSource(_machinedSurfaceField, NormalizeLegacyMachinedLevel2Field(
                settings.BomMachinedSurfaceField, "machined2:surface"));
            SelectFieldSource(_standardNameField, settings.BomStandardNameField);
            SelectFieldSource(_standardMaterialField, settings.BomStandardMaterialField);
            SelectFieldSource(_standardProcessField, settings.BomStandardProcessField);
            SelectFieldSource(_standardRemarkField, settings.BomStandardRemarkField);
            SelectFieldSource(_machinedNameField, settings.BomMachinedNameField);
            SelectFieldSource(_machinedMaterialField, NormalizeLegacyMachinedLevel2Field(
                settings.BomMachinedMaterialField, "machined2:material"));
            SelectFieldSource(_machinedProcessField, NormalizeLegacyMachinedLevel2Field(
                settings.BomMachinedProcessField, "machined2:process"));
            SelectFieldSource(_machinedRemarkField, settings.BomMachinedRemarkField);
            _columnOrder.Clear();
            _columnOrder.AddRange(PartListService.ParseColumnOrder(settings.BomColumnOrder));
            RebuildMappingTable();
            RefreshColumnOrderSelector(_columnOrder.Count > 0 ? _columnOrder[0] : string.Empty);
        }

        private void DetectAll()
        {
            _weldment.Text = SwFolders.WeldmentProfiles();
            _template.Text = SwFolders.DrawingTemplates();
            _macro.Text = SwFolders.Macros();
            _toolbox.Text = SwFolders.Toolbox();
            _status.Text = "已按 SOLIDWORKS 默认位置自动检测，确认后点「保存目录设置」。";
        }

        private void SaveSettings()
        {
            var settings = _host.Settings;
            settings.WeldmentFolder = _weldment.Text.Trim();
            settings.TemplateFolder = _template.Text.Trim();
            settings.MacroFolder = _macro.Text.Trim();
            settings.ToolboxFolder = _toolbox.Text.Trim();
            settings.BomPrefixes = _prefixes.Text.Trim();
            settings.BomRequirePattern = _requirePattern.Checked;
            settings.BomUsePropertyFields = _usePropertyFields.Checked;
            settings.BomAssemblyLevel = AssemblyLevelFromIndex(_assemblyLevel.SelectedIndex);
            settings.BomSequenceHeader = HeaderOrDefault(_sequenceHeader.Text, "序号");
            settings.BomLocationHeader = HeaderOrDefault(_locationHeader.Text, "位置");
            settings.BomFullNameHeader = HeaderOrDefault(_fullNameHeader.Text, "完整名称");
            settings.BomDrawingHeader = HeaderOrDefault(_drawingHeader.Text, "二维工程图");
            settings.BomClassificationHeader = HeaderOrDefault(_classificationHeader.Text, "属性");
            settings.BomNameHeader = HeaderOrDefault(_nameHeader.Text, "零件名称/标准件名称");
            settings.BomMaterialHeader = HeaderOrDefault(_materialHeader.Text, "材料/型号");
            settings.BomProcessHeader = HeaderOrDefault(_processHeader.Text, "工艺/渠道");
            settings.BomSurfaceHeader = HeaderOrDefault(_surfaceHeader.Text, "表面处理");
            settings.BomQuantityHeader = HeaderOrDefault(_quantityHeader.Text, "数量");
            settings.BomAssemblyNoteHeader = HeaderOrDefault(_assemblyNoteHeader.Text, "安装说明");
            settings.BomRemarkHeader = HeaderOrDefault(_remarkHeader.Text, "备注");
            settings.BomStandardAssemblyNoteField = FieldSourceCode(_standardAssemblyNoteField);
            settings.BomMachinedAssemblyNoteField = FieldSourceCode(_machinedAssemblyNoteField);
            settings.BomStandardSurfaceField = FieldSourceCode(_standardSurfaceField);
            settings.BomMachinedSurfaceField = FieldSourceCode(_machinedSurfaceField);
            settings.BomStandardNameField = FieldSourceCode(_standardNameField);
            settings.BomStandardMaterialField = FieldSourceCode(_standardMaterialField);
            settings.BomStandardProcessField = FieldSourceCode(_standardProcessField);
            settings.BomStandardRemarkField = FieldSourceCode(_standardRemarkField);
            settings.BomMachinedNameField = FieldSourceCode(_machinedNameField);
            settings.BomMachinedMaterialField = FieldSourceCode(_machinedMaterialField);
            settings.BomMachinedProcessField = FieldSourceCode(_machinedProcessField);
            settings.BomMachinedRemarkField = FieldSourceCode(_machinedRemarkField);
            settings.BomColumnOrder = PartListService.SerializeColumnOrder(_columnOrder);
            settings.Save();
            _status.Text = "设置已保存。";
        }

        private void RebuildFieldSourceCombos()
        {
            var combos = new[]
            {
                _standardAssemblyNoteField, _standardNameField, _standardMaterialField,
                _standardProcessField, _standardRemarkField,
                _standardSurfaceField,
                _machinedAssemblyNoteField, _machinedNameField, _machinedMaterialField,
                _machinedProcessField, _machinedSurfaceField, _machinedRemarkField
            };
            foreach (var combo in combos)
            {
                var selected = FieldSourceCode(combo);
                var standard = combo.Tag is bool && (bool)combo.Tag;
                PopulateFieldSourceCombo(combo, standard, _usePropertyFields.Checked);
                SelectFieldSource(combo, selected);
            }
        }

        private static string HeaderOrDefault(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string FieldSourceCode(ComboBox combo)
        {
            var item = combo == null ? null : combo.SelectedItem as FieldSourceItem;
            return item == null || string.IsNullOrWhiteSpace(item.Code) ? "auto" : item.Code;
        }

        private static string NormalizeLegacyMachinedLevel2Field(string value, string expandedCode)
        {
            var source = (value ?? string.Empty).Trim();
            if (string.Equals(source, "rule:material", StringComparison.OrdinalIgnoreCase) ||
                (expandedCode == "machined2:surface" &&
                 string.Equals(source, "rule:surface", StringComparison.OrdinalIgnoreCase)))
            {
                return expandedCode;
            }
            return source;
        }

        private static void SelectFieldSource(ComboBox combo, string code)
        {
            if (combo == null)
            {
                return;
            }

            var value = (code ?? "auto").Trim().ToLowerInvariant();
            for (var index = 0; index < combo.Items.Count; index++)
            {
                var item = combo.Items[index] as FieldSourceItem;
                if (item != null && string.Equals(item.Code, value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = index;
                    return;
                }
            }

            // 旧版标准件可能保存为 segment:3..8；三级规则统一迁移为完整型号 tail:3。
            var standard = combo.Tag is bool && (bool)combo.Tag;
            if (!standard)
            {
                if (value == "rule:surface")
                {
                    value = "machined2:surface";
                }

                for (var index = 0; index < combo.Items.Count; index++)
                {
                    var item = combo.Items[index] as FieldSourceItem;
                    if (item != null && string.Equals(item.Code, value,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        combo.SelectedIndex = index;
                        return;
                    }
                }
            }
            if (standard && value.StartsWith("segment:", StringComparison.Ordinal))
            {
                int segment;
                if (int.TryParse(value.Substring("segment:".Length), out segment) && segment >= 3)
                {
                    for (var index = 0; index < combo.Items.Count; index++)
                    {
                        var item = combo.Items[index] as FieldSourceItem;
                        if (item != null && item.Code == "tail:3")
                        {
                            combo.SelectedIndex = index;
                            return;
                        }
                    }
                }
            }

            combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
        }

        private static int AssemblyLevelToIndex(int value)
        {
            if (value <= 0) return value < 0 ? 2 : 0;
            return value == 1 ? 1 : 2;
        }

        private static int AssemblyLevelFromIndex(int index)
        {
            if (index <= 0) return 0;
            return index == 1 ? 1 : -1;
        }

        /// <summary>把随包的焊件轮廓库一键迁移到 SOLIDWORKS（会弹一次 UAC）。</summary>
        private void InstallWeldmentLibrary()
        {
            var source = WeldmentLibrary.FindSource(_host.Settings);
            var target = WeldmentLibrary.TargetRoot;

            var confirm = string.Format(
                "将把这套焊件轮廓库安装到 SOLIDWORKS：{0}{0}源目录：{1}{0}目标目录：{2}{0}{0}" +
                "目标在 Program Files 下，会弹出一次 UAC 提权。是否继续？",
                Environment.NewLine,
                string.IsNullOrEmpty(source) ? "（未找到，将提示）" : source,
                string.IsNullOrEmpty(target) ? "（未检测到）" : target);

            if (MessageBox.Show(this, confirm, "MechKit",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            _status.Text = "正在迁移焊件库，请在弹出的 UAC 窗口点「是」…";

            string message;
            var ok = WeldmentLibrary.Install(_host.Settings, out message);
            _status.Text = ok ? "焊件库迁移完成。" : message;
            Log.Info(message);

            MessageBox.Show(this, message, "MechKit",
                MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void Export()
        {
            SaveSettings();

            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "导出 MechKit 个人设置";
                dialog.Filter = "MechKit 设置备份|*" + SettingsTransfer.FileExtension + "|注册表文件|*.reg";
                dialog.FileName = string.Format("MechKit设置-{0:yyyyMMdd-HHmmss}{1}",
                    DateTime.Now, SettingsTransfer.FileExtension);

                var folder = _host.Settings.SettingsBackupFolder;
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    dialog.InitialDirectory = folder;
                }

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                string message;
                var ok = SettingsTransfer.Export(dialog.FileName, true, out message);
                _status.Text = message;
                Log.Info(message);

                var settings = _host.Settings;
                settings.SettingsBackupFolder = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
                settings.Save();

                MessageBox.Show(this, message, "MechKit",
                    MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
        }

        private void Import()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "导入 MechKit 个人设置";
                dialog.Filter = "MechKit 设置备份|*" + SettingsTransfer.FileExtension + ";*.reg|所有文件|*.*";

                var folder = _host.Settings.SettingsBackupFolder;
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    dialog.InitialDirectory = folder;
                }

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var confirm = string.Format(
                    "将把该文件里的个人设置导入当前用户：{0}{1}{1}界面布局、笔势、快捷键、文件位置都会被覆盖，导入后需要重启 SOLIDWORKS。{1}{1}是否继续？",
                    Path.GetFileName(dialog.FileName), Environment.NewLine);

                if (MessageBox.Show(this, confirm, "MechKit",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }

                string message;
                var ok = SettingsTransfer.Import(dialog.FileName, out message);
                _status.Text = message;
                Log.Info(message);

                MessageBox.Show(this, message, "MechKit",
                    MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            }
        }

        private void OpenFolder(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    Process.Start("explorer.exe", path);
                    _status.Text = "已打开：" + path;
                }
                else
                {
                    _status.Text = "目录不存在：" + path;
                }
            }
            catch (Exception ex)
            {
                _status.Text = "打开失败：" + ex.Message;
            }
        }
    }
}
