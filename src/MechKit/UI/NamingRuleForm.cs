using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MechKit.Core;

namespace MechKit.UI
{
    /// <summary>
    /// 命名规则设置：分成「加工件」「标准件」两栏（选项卡），互不挤压。
    ///   · 加工件：可增删、排序的分段规则（时间段固定第一）、图号来源与截断
    ///   · 标准件：维护前缀、说明与中文中间名；实际套用统一在选项卡/任务面板完成
    /// </summary>
    internal sealed class NamingRuleForm : Form
    {
        private readonly IAddinHost _host;

        // 加工件栏
        private readonly TextBox _separator;
        private readonly TableLayoutPanel _segmentPanel;
        private readonly List<MachinedSegmentKind> _machinedSegments;
        private readonly List<string> _segmentLabels;
        private readonly List<bool> _segmentBomNameFlags;
        private readonly ComboBox _partNumberSource;
        private readonly ComboBox _cutRule;
        private readonly TextBox _pattern;
        private readonly TextBox _machinedMaterialProcessRules;
        private readonly TextBox _machinedLevel2Values;
        private readonly TextBox _newMachinedLevel2;
        private readonly TableLayoutPanel _machinedLevel2Panel = new TableLayoutPanel();
        private readonly List<Level2Row> _machinedLevel2Rows = new List<Level2Row>();
        private bool _syncingLevel2;
        private readonly HashSet<string> _machinedTabLevel2Values;
        private readonly TextBox _machinedLevel3Values;
        private readonly HashSet<string> _machinedTabLevel3Values;

        // 标准件栏
        private readonly TextBox _prefixes;
        private readonly TextBox _newPrefix;
        private readonly TableLayoutPanel _usedPrefixPanel;
        private readonly Dictionary<string, string> _prefixDescriptions;
        private readonly HashSet<string> _standardTabPrefixes;
        private readonly TextBox _middleNames;
        private readonly TextBox _newMiddleName;
        private readonly TableLayoutPanel _usedMiddleNamePanel;
        private readonly Dictionary<string, string> _middleNameDescriptions;
        private readonly HashSet<string> _standardTabMiddleNames;
        private readonly CheckBox _standardBindingEnabled;
        private readonly TextBox _standardBindings;

        private bool _syncing;
        private readonly int _initialTab;

        public NamingRuleForm(IAddinHost host) : this(host, 0)
        {
        }

        public NamingRuleForm(IAddinHost host, int initialTab)
        {
            _host = host;
            _initialTab = initialTab;
            _separator = Theme.CreateTextBox();
            _separator.ReadOnly = true;
            _segmentPanel = new TableLayoutPanel();
            _machinedSegments = new List<MachinedSegmentKind>();
            _segmentLabels = new List<string>();
            _segmentBomNameFlags = new List<bool>();
            _partNumberSource = new ComboBox();
            _cutRule = new ComboBox();
            _pattern = Theme.CreateTextBox();
            _machinedMaterialProcessRules = Theme.CreateTextBox();
            _machinedMaterialProcessRules.Multiline = true;
            _machinedMaterialProcessRules.ScrollBars = ScrollBars.Vertical;
            _machinedMaterialProcessRules.AcceptsReturn = true;
            _machinedLevel2Values = Theme.CreateTextBox();
            _newMachinedLevel2 = Theme.CreateTextBox();
            _machinedTabLevel2Values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _machinedLevel3Values = Theme.CreateTextBox();
            _machinedTabLevel3Values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _prefixes = Theme.CreateTextBox();
            _newPrefix = Theme.CreateTextBox();
            _usedPrefixPanel = new TableLayoutPanel();
            _prefixDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _standardTabPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _middleNames = Theme.CreateTextBox();
            _newMiddleName = Theme.CreateTextBox();
            _usedMiddleNamePanel = new TableLayoutPanel();
            _middleNameDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _standardTabMiddleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _standardBindingEnabled = new CheckBox();
            _standardBindings = Theme.CreateTextBox();

            BuildLayout();
            LoadFromSettings();
            UpdatePrefixButtons();
            UpdateMiddleNameButtons();
        }

        private void BuildLayout()
        {
            InitPartNumberControls();
            Text = "命名规则设置 - MechKit";
            Font = Theme.Body;
            BackColor = Theme.Canvas;
            WindowLayout.Attach(this, _host.Settings, new Size(1080, 860), new Size(900, 700));

            var header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Theme.Accent };
            var title = Theme.CreateLabel("命名规则设置", Theme.Title, Color.White);
            title.Location = new Point(14, 9);
            var subtitle = Theme.CreateLabel("新名称统一用 - 分段；识别时兼容旧下划线 _",
                Theme.Small, Color.FromArgb(214, 232, 248));
            subtitle.Location = new Point(15, 32);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 6), BackColor = Theme.Canvas };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 1,
                BackColor = Theme.Canvas
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var tabs = (TabControl)BuildTabs();
            layout.Controls.Add(tabs, 0, 0);
            body.Controls.Add(layout);
            WireNamingEditors();

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.Canvas, Padding = new Padding(14, 6, 14, 12) };
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                BackColor = Theme.Canvas
            };

            var reset = Theme.CreateSecondaryButton("恢复默认");
            reset.Width = 96;
            reset.Click += delegate { ResetToDefault(); };

            var save = Theme.CreatePrimaryButton("保存");
            save.Width = 96;
            save.Click += delegate { Save(); };

            var cancel = Theme.CreateSecondaryButton("取消");
            cancel.Width = 88;
            cancel.Click += delegate { Close(); };

            buttons.Controls.Add(reset);
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            footer.Controls.Add(buttons);

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);
        }

        /// <summary>两栏：加工件 / 标准件。</summary>
        private Control BuildTabs()
        {
            var tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = Theme.Body,
                Padding = new Point(18, 6)
            };

            var partPage = new TabPage("加工件（日期开头）") { BackColor = Theme.Surface, Padding = new Padding(12, 14, 12, 12) };
            partPage.Controls.Add(BuildMachinedPanel());

            var standardPage = new TabPage("标准件（前缀开头）") { BackColor = Theme.Surface, Padding = new Padding(12, 14, 12, 12) };
            standardPage.Controls.Add(BuildStandardPanel());

            tabs.TabPages.Add(partPage);
            tabs.TabPages.Add(standardPage);
            tabs.SelectedIndex = _initialTab == 1 ? 1 : 0;
            return tabs;
        }

        private Control BuildMachinedPanel()
        {
            var viewport = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Surface,
                AutoScroll = true
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 6,
                BackColor = Theme.Surface,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0, 0, 6, 8)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var title = Theme.CreateLabel("加工件命名规则（按段顺序进入 BOM）", Theme.BodyBold, Theme.Text);
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(BuildField("分段符号", _separator,
                "新名称使用短横线 -；读取时兼容下划线 _"), 0, 1);

            var segmentCaption = Theme.CreateLabel(
                "加工件分段（使用 ↑ / ↓ 排序；勾选后并入 BOM 零件名）", Theme.Body, Theme.Text);
            segmentCaption.Dock = DockStyle.Fill;
            segmentCaption.TextAlign = ContentAlignment.BottomLeft;
            segmentCaption.Margin = new Padding(0, 4, 0, 3);
            layout.Controls.Add(segmentCaption, 0, 2);

            _segmentPanel.Dock = DockStyle.Top;
            _segmentPanel.ColumnCount = 1;
            _segmentPanel.AutoSize = true;
            _segmentPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _segmentPanel.BackColor = Theme.Surface;
            _segmentPanel.Margin = new Padding(0);
            _segmentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.Controls.Add(_segmentPanel, 0, 3);

            var addSegment = Theme.CreateSecondaryButton("＋ 增加段");
            addSegment.Width = 108;
            addSegment.Height = 30;
            addSegment.Margin = new Padding(0, 5, 0, 5);
            addSegment.Click += delegate
            {
                if (_machinedSegments.Count >= 12)
                {
                    MessageBox.Show(this, "最多可以设置 12 个段。", AddinConstants.Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                _machinedSegments.Add(MachinedSegmentKind.Custom);
                _segmentLabels.Add("自定义段");
                _segmentBomNameFlags.Add(false);
                RebuildSegmentRows();
            };
            var addRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Surface
            };
            addRow.Controls.Add(addSegment);
            layout.Controls.Add(addRow, 0, 4);

            // 材料段二级解析与二级快捷按钮合并成一张表：一行 = 材料段 + 材料/工艺/表面处理 + 是否上选项卡。
            layout.Controls.Add(BuildMachinedLevel2Editor(), 0, 5);

            viewport.Controls.Add(layout);
            return viewport;
        }

        /// <summary>
        /// 图号来源 / 截断规则已不在界面上编辑，但设置仍会原样读写，
        /// 因此这里只把下拉项准备好，保证载入与保存的索引都有效。
        /// </summary>
        private void InitPartNumberControls()
        {
            _partNumberSource.DropDownStyle = ComboBoxStyle.DropDownList;
            _partNumberSource.Font = Theme.Body;
            _partNumberSource.Items.AddRange(new object[]
            {
                "用零件文件名（推荐）",
                "优先读自定义属性「图号/零件号」",
                "只用自定义属性"
            });

            _cutRule.DropDownStyle = ComboBoxStyle.DropDownList;
            _cutRule.Font = Theme.Body;
            _cutRule.Items.AddRange(new object[]
            {
                "完整文件名",
                "第一个空格前",
                "第一个下划线前",
                "第一个短横线前",
                "自定义正则"
            });
        }

        /// <summary>
        /// 材料段二级解析 + 二级字段快捷按钮（材料）：合并成一张表。
        /// 一行 = 文件名材料段取值 + 材料 / 工艺 / 表面处理（BOM 解析）+ 是否显示在快捷区。
        /// </summary>
        private Control BuildMachinedLevel2Editor()
        {
            var section = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.FromArgb(248, 249, 250),
                Margin = new Padding(0, 5, 0, 5),
                Padding = new Padding(8, 5, 8, 7)
            };
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            section.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));

            var titleLabel = Theme.CreateLabel("材料段二级解析与快捷按钮（材料）", Theme.BodyBold, Theme.Text);
            titleLabel.Dock = DockStyle.Fill;
            section.Controls.Add(titleLabel, 0, 0);

            var hintLabel = CreateWrappedHint(
                "每行一个材料段：材料 / 工艺 / 表面处理用于解析 BOM；勾选“选项卡”后显示在快捷区上排");
            section.Controls.Add(hintLabel, 0, 1);

            _machinedLevel2Panel.Dock = DockStyle.Top;
            _machinedLevel2Panel.AutoSize = true;
            _machinedLevel2Panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _machinedLevel2Panel.ColumnCount = 1;
            _machinedLevel2Panel.RowCount = 0;
            _machinedLevel2Panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _machinedLevel2Panel.BackColor = Color.Transparent;
            _machinedLevel2Panel.Margin = new Padding(0, 2, 0, 2);
            section.Controls.Add(_machinedLevel2Panel, 0, 2);

            // 与原来的「二级字段快捷按钮」一样：输入材料段后点“增加”就多一行。
            var addRowPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            addRowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,
                TextWidth("增加材料段", Theme.Body, 18)));
            addRowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260f));
            addRowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));
            addRowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var addCaption = Theme.CreateFieldLabel("增加材料段");
            addCaption.Dock = DockStyle.Fill;
            _newMachinedLevel2.Dock = DockStyle.Fill;
            _newMachinedLevel2.Margin = new Padding(0, 6, 10, 6);
            var addButton = Theme.CreatePrimaryButton("＋ 增加");
            addButton.Dock = DockStyle.Fill;
            addButton.Margin = new Padding(0, 5, 10, 5);
            addButton.Click += delegate { AddTypedMachinedLevel2(); };
            _newMachinedLevel2.KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.KeyCode == Keys.Enter)
                {
                    AddTypedMachinedLevel2();
                    args.Handled = true;
                    args.SuppressKeyPress = true;
                }
            };
            var addHint = Theme.CreateValueLabel("勾选后保存，立即显示在快捷区上排；材料段顺序就是按钮顺序");
            addHint.Dock = DockStyle.Fill;
            addHint.Font = Theme.Small;
            addHint.ForeColor = Theme.Muted;
            addRowPanel.Controls.Add(addCaption, 0, 0);
            addRowPanel.Controls.Add(_newMachinedLevel2, 1, 0);
            addRowPanel.Controls.Add(addButton, 2, 0);
            addRowPanel.Controls.Add(addHint, 3, 0);
            section.Controls.Add(addRowPanel, 0, 3);

            return section;
        }

        /// <summary>合并表格里的一行：材料段 + 材料/工艺/表面处理 + 是否上选项卡。</summary>
        private sealed class Level2Row
        {
            public TextBox Token;
            public TextBox Material;
            public TextBox Process;
            public TextBox Surface;
            public CheckBox ShowOnTab;
        }

        private static void ApplyMachinedLevel2Columns(TableLayoutPanel panel)
        {
            // 列宽按表头文字测量：材料段列要放得下“材料段（留空=材料）”，
            // 选项卡列要放得下复选框 + “选项卡”三个字。
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,
                Math.Max(150, TextWidth("材料段（留空=材料）", Theme.Small, 20))));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24f));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24f));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32f));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,
                Math.Max(84, TextWidth("选项卡", Theme.Body, 52))));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42f));
        }

        /// <summary>按存储内容重建合并表格（载入设置 / 恢复默认 / 删除行时调用）。</summary>
        private void RebuildMachinedLevel2Rows()
        {
            if (_syncingLevel2)
            {
                return;
            }

            _syncingLevel2 = true;
            _machinedLevel2Panel.SuspendLayout();
            try
            {
                foreach (Control control in new List<Control>(GetControls(_machinedLevel2Panel)))
                {
                    _machinedLevel2Panel.Controls.Remove(control);
                    control.Dispose();
                }
                _machinedLevel2Panel.RowStyles.Clear();
                _machinedLevel2Panel.RowCount = 0;
                _machinedLevel2Rows.Clear();

                // 材料段顺序 = 快捷按钮顺序；只出现在解析表里的取值补到后面。
                var presets = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in ParseMaterialPresetLines(_machinedMaterialProcessRules.Text))
                {
                    if (!presets.ContainsKey(line[0]))
                    {
                        presets[line[0]] = line;
                    }
                }

                var order = new List<string>();
                foreach (var value in NamingOptionsFactory.ParsePrefixes(_machinedLevel2Values.Text))
                {
                    if (!order.Contains(value))
                    {
                        order.Add(value);
                    }
                }
                foreach (var token in presets.Keys)
                {
                    if (!order.Contains(token))
                    {
                        order.Add(token);
                    }
                }

                AddMachinedLevel2HeaderRow();
                foreach (var token in order)
                {
                    string[] preset;
                    presets.TryGetValue(token, out preset);
                    AddMachinedLevel2Row(token,
                        preset == null ? string.Empty : preset[1],
                        preset == null ? string.Empty : preset[2],
                        preset == null ? string.Empty : preset[3],
                        _machinedTabLevel2Values.Contains(token));
                }

                if (_machinedLevel2Rows.Count == 0)
                {
                    AddMachinedLevel2Row(string.Empty, string.Empty, string.Empty, string.Empty, true);
                }
            }
            finally
            {
                _machinedLevel2Panel.ResumeLayout(true);
                _syncingLevel2 = false;
            }
        }

        private void AddMachinedLevel2HeaderRow()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 6,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            ApplyMachinedLevel2Columns(panel);

            var captions = new[] { "材料段（留空=材料）", "材料", "工艺", "表面处理", "选项卡" };
            for (var index = 0; index < captions.Length; index++)
            {
                var label = Theme.CreateLabel(captions[index], Theme.Small, Theme.Muted);
                label.Dock = DockStyle.Fill;
                label.TextAlign = ContentAlignment.MiddleLeft;
                label.Margin = new Padding(index == 0 ? 0 : 2, 0, 6, 0);
                panel.Controls.Add(label, index, 0);
            }

            _machinedLevel2Panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _machinedLevel2Panel.RowCount++;
            _machinedLevel2Panel.Controls.Add(panel, 0, _machinedLevel2Panel.RowCount - 1);
        }

        private Level2Row AddMachinedLevel2Row(string token, string material, string process,
            string surface, bool showOnTab)
        {
            var row = new Level2Row
            {
                Token = Theme.CreateTextBox(),
                Material = Theme.CreateTextBox(),
                Process = Theme.CreateTextBox(),
                Surface = Theme.CreateTextBox(),
                ShowOnTab = new CheckBox
                {
                    Text = "选项卡",
                    Checked = showOnTab,
                    AutoSize = true,
                    Anchor = AnchorStyles.None,
                    ForeColor = Theme.Text,
                    Margin = new Padding(2, 0, 2, 0)
                }
            };
            row.Token.Text = token ?? string.Empty;
            row.Material.Text = material ?? string.Empty;
            row.Process.Text = process ?? string.Empty;
            row.Surface.Text = surface ?? string.Empty;

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                ColumnCount = 6,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 1, 0, 1)
            };
            ApplyMachinedLevel2Columns(panel);

            foreach (var box in new[] { row.Token, row.Material, row.Process, row.Surface })
            {
                box.Dock = DockStyle.Fill;
                box.Margin = new Padding(0, 2, 6, 2);
                box.TextChanged += delegate { SyncMachinedLevel2(); };
            }
            row.ShowOnTab.CheckedChanged += delegate { SyncMachinedLevel2(); };

            var delete = Theme.CreateSecondaryButton("×");
            delete.Dock = DockStyle.Fill;
            delete.Margin = new Padding(0, 2, 0, 2);
            delete.Click += delegate
            {
                _machinedLevel2Rows.Remove(row);
                SyncMachinedLevel2();
                RebuildMachinedLevel2Rows();
            };

            panel.Controls.Add(row.Token, 0, 0);
            panel.Controls.Add(row.Material, 1, 0);
            panel.Controls.Add(row.Process, 2, 0);
            panel.Controls.Add(row.Surface, 3, 0);
            panel.Controls.Add(row.ShowOnTab, 4, 0);
            panel.Controls.Add(delete, 5, 0);

            _machinedLevel2Panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            _machinedLevel2Panel.RowCount++;
            _machinedLevel2Panel.Controls.Add(panel, 0, _machinedLevel2Panel.RowCount - 1);
            _machinedLevel2Rows.Add(row);
            return row;
        }

        /// <summary>
        /// 合并表格 → 三份存储：材料段列表、上选项卡的集合、材料/工艺/表面处理解析表。
        /// </summary>
        private void SyncMachinedLevel2()
        {
            if (_syncingLevel2)
            {
                return;
            }

            var tokens = new List<string>();
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var presetRows = new List<string[]>();
            foreach (var row in _machinedLevel2Rows)
            {
                var token = row.Token.Text.Trim().Trim('_', '-');
                if (token.Length == 0)
                {
                    continue;
                }

                var material = row.Material.Text.Trim();
                var process = row.Process.Text.Trim();
                var surface = row.Surface.Text.Trim();
                if (!tokens.Contains(token))
                {
                    tokens.Add(token);
                }
                if (row.ShowOnTab.Checked)
                {
                    selected.Add(token);
                }
                if (material.Length > 0 || process.Length > 0 || surface.Length > 0)
                {
                    presetRows.Add(new[] { token, material, process, surface });
                }
            }

            _syncingLevel2 = true;
            try
            {
                _machinedLevel2Values.Text = NamingOptionsFactory.SerializePrefixes(tokens);
                _machinedMaterialProcessRules.Text =
                    string.Join("\r\n", BuildMaterialPresetLines(presetRows).ToArray());
                _machinedTabLevel2Values.Clear();
                foreach (var token in selected)
                {
                    _machinedTabLevel2Values.Add(token);
                }
            }
            finally
            {
                _syncingLevel2 = false;
            }
        }

        private void AddTypedMachinedLevel2()
        {
            var value = _newMachinedLevel2.Text.Trim().Trim('_', '-');
            if (value.Length == 0)
            {
                return;
            }

            foreach (var row in _machinedLevel2Rows)
            {
                if (string.Equals(row.Token.Text.Trim(), value, StringComparison.OrdinalIgnoreCase))
                {
                    _newMachinedLevel2.Clear();
                    row.Token.Focus();
                    return;
                }
            }

            var existing = NamingOptionsFactory.ParsePrefixes(_machinedLevel2Values.Text);
            if (existing.Length >= AddinConstants.MaxMachinedLevel2Commands)
            {
                MessageBox.Show(this, "选项卡面板最多显示 12 个材料字段，请先删除一个再增加。",
                    AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var added = AddMachinedLevel2Row(value, string.Empty, string.Empty, string.Empty, true);
            SyncMachinedLevel2();
            _newMachinedLevel2.Clear();
            _newMachinedLevel2.Focus();
            if (added != null)
            {
                added.Material.Focus();
            }
        }

        /// <summary>
        /// 表格行 → 存储文本：材料段=材料|工艺|表面处理。
        /// 材料段留空时按“材料”列取值，因此只填材料/工艺/表面处理三列也能正常工作；
        /// 整行留空的行会被忽略。
        /// </summary>
        internal static List<string> BuildMaterialPresetLines(IList<string[]> rows)
        {
            var lines = new List<string>();
            if (rows == null)
            {
                return lines;
            }

            foreach (var row in rows)
            {
                if (row == null || row.Length < 4)
                {
                    continue;
                }

                var token = (row[0] ?? string.Empty).Trim();
                var material = (row[1] ?? string.Empty).Trim();
                var process = (row[2] ?? string.Empty).Trim();
                var surface = (row[3] ?? string.Empty).Trim();
                if (token.Length == 0 && material.Length == 0 && process.Length == 0 && surface.Length == 0)
                {
                    continue;
                }

                if (token.Length == 0)
                {
                    token = material;
                }
                if (token.Length == 0)
                {
                    continue;
                }

                lines.Add(token + "=" + material + "|" + process + "|" + surface);
            }

            return lines;
        }

        /// <summary>把存储文本解析成表格行：token=材料|工艺|表面处理，兼容旧的 token=材料-工艺。</summary>
        private static List<string[]> ParseMaterialPresetLines(string text)
        {
            var rows = new List<string[]>();
            foreach (var raw in (text ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                var equal = line.IndexOf('=');
                if (equal <= 0)
                {
                    continue;
                }

                var token = line.Substring(0, equal).Trim();
                var payload = line.Substring(equal + 1).Trim();
                string material;
                string process;
                string surface;
                if (payload.IndexOf('|') >= 0)
                {
                    var fields = payload.Split('|');
                    material = fields.Length > 0 ? fields[0].Trim() : string.Empty;
                    process = fields.Length > 1 ? fields[1].Trim() : string.Empty;
                    surface = fields.Length > 2 ? fields[2].Trim() : string.Empty;
                }
                else
                {
                    var separator = payload.LastIndexOf('-');
                    if (separator <= 0)
                    {
                        material = payload;
                        process = string.Empty;
                    }
                    else
                    {
                        material = payload.Substring(0, separator).Trim();
                        process = payload.Substring(separator + 1).Trim();
                    }
                    surface = string.Empty;
                }

                rows.Add(new[] { token, material, process, surface });
            }

            return rows;
        }


        private void RebuildSegmentRows()
        {
            while (_segmentLabels.Count < _machinedSegments.Count)
            {
                _segmentLabels.Add(string.Empty);
            }

            while (_segmentLabels.Count > _machinedSegments.Count)
            {
                _segmentLabels.RemoveAt(_segmentLabels.Count - 1);
            }
            while (_segmentBomNameFlags.Count < _machinedSegments.Count)
            {
                _segmentBomNameFlags.Add(
                    _machinedSegments[_segmentBomNameFlags.Count] == MachinedSegmentKind.Name);
            }
            while (_segmentBomNameFlags.Count > _machinedSegments.Count)
            {
                _segmentBomNameFlags.RemoveAt(_segmentBomNameFlags.Count - 1);
            }

            _segmentPanel.SuspendLayout();
            try
            {
                foreach (Control control in new List<Control>(GetControls(_segmentPanel)))
                {
                    _segmentPanel.Controls.Remove(control);
                    control.Dispose();
                }

                _segmentPanel.RowStyles.Clear();
                _segmentPanel.RowCount = _machinedSegments.Count;
                for (var index = 0; index < _machinedSegments.Count; index++)
                {
                    _segmentPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
                    _segmentPanel.Controls.Add(BuildSegmentRow(index), 0, index);
                }
            }
            finally
            {
                _segmentPanel.ResumeLayout(true);
            }
        }

        private Control BuildSegmentRow(int index)
        {
            var kind = _machinedSegments[index];
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 7,
                RowCount = 1,
                BackColor = index % 2 == 0 ? Color.FromArgb(248, 249, 250) : Theme.Surface,
                Margin = new Padding(0, 1, 0, 1),
                Padding = new Padding(4, 3, 4, 3)
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,
                Math.Max(190, TextWidth("时间段（加工件判定）", Theme.Body, 46))));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,
                Math.Max(126, TextWidth("并入 BOM 名称", Theme.Body, 40))));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38f));

            var order = Theme.CreateValueLabel((index + 1).ToString() + ".");
            order.Dock = DockStyle.Fill;
            order.Font = Theme.BodyBold;
            order.TextAlign = ContentAlignment.MiddleCenter;

            var selector = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = Theme.Body,
                Margin = new Padding(0)
            };

            if (kind == MachinedSegmentKind.Date)
            {
                selector.Items.Add("时间段（加工件判定）");
                selector.SelectedIndex = 0;
                selector.Enabled = false;
            }
            else
            {
                selector.Items.AddRange(new object[]
                {
                    "材料段", "零件名称段", "版本号段", "拓展代号段", "自定义段"
                });
                selector.DropDownStyle = kind == MachinedSegmentKind.Custom
                    ? ComboBoxStyle.DropDown
                    : ComboBoxStyle.DropDownList;
                if (kind == MachinedSegmentKind.Custom)
                {
                    selector.Text = string.IsNullOrWhiteSpace(_segmentLabels[index])
                        ? "自定义段"
                        : _segmentLabels[index];
                }
                else
                {
                    selector.SelectedIndex = SegmentSelectorIndex(kind);
                }

                var capturedIndex = index;
                selector.SelectedIndexChanged += delegate
                {
                    if (selector.SelectedIndex < 0 || capturedIndex >= _machinedSegments.Count)
                    {
                        return;
                    }

                    var selectedKind = SegmentKindFromSelector(selector.SelectedIndex);
                    _machinedSegments[capturedIndex] = selectedKind;
                    _segmentLabels[capturedIndex] = selectedKind == MachinedSegmentKind.Custom
                        ? "自定义段"
                        : string.Empty;
                    _segmentBomNameFlags[capturedIndex] = selectedKind == MachinedSegmentKind.Name;
                    RebuildSegmentRows();
                };
                selector.TextChanged += delegate
                {
                    if (capturedIndex < _machinedSegments.Count &&
                        _machinedSegments[capturedIndex] == MachinedSegmentKind.Custom &&
                        selector.SelectedIndex < 0)
                    {
                        _segmentLabels[capturedIndex] = selector.Text;
                    }
                };
            }

            var hint = Theme.CreateValueLabel(SegmentHint(kind));
            hint.Dock = DockStyle.Fill;
            hint.Font = Theme.Small;
            hint.ForeColor = Theme.Muted;
            hint.Padding = new Padding(8, 0, 4, 0);

            var includeInBomName = new CheckBox
            {
                Text = "并入 BOM 名称",
                Dock = DockStyle.Fill,
                AutoSize = true,
                Checked = index < _segmentBomNameFlags.Count && _segmentBomNameFlags[index],
                Enabled = kind != MachinedSegmentKind.Date && kind != MachinedSegmentKind.Material,
                ForeColor = Theme.Text,
                Margin = new Padding(3, 3, 0, 0)
            };
            var includeIndex = index;
            includeInBomName.CheckedChanged += delegate
            {
                if (includeIndex < _segmentBomNameFlags.Count)
                {
                    _segmentBomNameFlags[includeIndex] = includeInBomName.Checked;
                }
            };

            var up = CreateSegmentButton("↑", kind != MachinedSegmentKind.Date && index > 1);
            var down = CreateSegmentButton("↓", kind != MachinedSegmentKind.Date && index < _machinedSegments.Count - 1);
            var remove = CreateSegmentButton("×", kind != MachinedSegmentKind.Date);
            var rowIndex = index;
            up.Click += delegate { MoveSegment(rowIndex, -1); };
            down.Click += delegate { MoveSegment(rowIndex, 1); };
            remove.Click += delegate { RemoveSegment(rowIndex); };

            row.Controls.Add(order, 0, 0);
            row.Controls.Add(selector, 1, 0);
            row.Controls.Add(hint, 2, 0);
            row.Controls.Add(includeInBomName, 3, 0);
            row.Controls.Add(up, 4, 0);
            row.Controls.Add(down, 5, 0);
            row.Controls.Add(remove, 6, 0);
            return row;
        }

        private static Button CreateSegmentButton(string text, bool enabled)
        {
            var button = Theme.CreateSecondaryButton(text);
            button.Dock = DockStyle.Fill;
            button.Enabled = enabled;
            button.Margin = new Padding(2, 0, 0, 0);
            return button;
        }

        private void MoveSegment(int index, int offset)
        {
            var target = index + offset;
            if (index < 0 || index >= _machinedSegments.Count || target <= 0 ||
                target >= _machinedSegments.Count || _machinedSegments[index] == MachinedSegmentKind.Date)
            {
                return;
            }

            var item = _machinedSegments[index];
            var label = _segmentLabels[index];
            var includeInBomName = _segmentBomNameFlags[index];
            _machinedSegments.RemoveAt(index);
            _segmentLabels.RemoveAt(index);
            _segmentBomNameFlags.RemoveAt(index);
            _machinedSegments.Insert(target, item);
            _segmentLabels.Insert(target, label);
            _segmentBomNameFlags.Insert(target, includeInBomName);
            RebuildSegmentRows();
        }

        private void RemoveSegment(int index)
        {
            if (index < 0 || index >= _machinedSegments.Count ||
                _machinedSegments[index] == MachinedSegmentKind.Date)
            {
                return;
            }

            _machinedSegments.RemoveAt(index);
            _segmentLabels.RemoveAt(index);
            _segmentBomNameFlags.RemoveAt(index);
            RebuildSegmentRows();
        }

        private static int SegmentSelectorIndex(MachinedSegmentKind kind)
        {
            switch (kind)
            {
                case MachinedSegmentKind.Name: return 1;
                case MachinedSegmentKind.Serial: return 2;
                case MachinedSegmentKind.Extension: return 3;
                case MachinedSegmentKind.Custom: return 4;
                default: return 0;
            }
        }

        private static MachinedSegmentKind SegmentKindFromSelector(int index)
        {
            switch (index)
            {
                case 1: return MachinedSegmentKind.Name;
                case 2: return MachinedSegmentKind.Serial;
                case 3: return MachinedSegmentKind.Extension;
                case 4: return MachinedSegmentKind.Custom;
                default: return MachinedSegmentKind.Material;
            }
        }

        private static string SegmentHint(MachinedSegmentKind kind)
        {
            switch (kind)
            {
                case MachinedSegmentKind.Date: return "6–8 位日期；此段用于判定加工件";
                case MachinedSegmentKind.Material: return "材料牌号，例如 6061 / 5052 / 304";
                case MachinedSegmentKind.Name: return "解析为零件名称";
                case MachinedSegmentKind.Serial: return "版本号，例如 A / B / 01";
                case MachinedSegmentKind.Extension: return "拓展代号，例如 X / L / 项目代号";
                default: return "可直接输入自定义段名称";
            }
        }

        private Control BuildStandardPanel()
        {
            var viewport = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Surface,
                AutoScroll = true
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 7,
                BackColor = Theme.Surface,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0, 0, 6, 8)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var title = Theme.CreateLabel("标准件一级字段管理（渠道/前缀）", Theme.BodyBold, Theme.Text);
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.Margin = new Padding(0, 0, 0, 4);
            layout.Controls.Add(title, 0, 0);

            var addRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = Theme.Surface
            };
            addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,
                TextWidth("增加一级字段", Theme.Body, 18)));
            addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260f));
            addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));
            addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var addCaption = Theme.CreateFieldLabel("增加一级字段");
            addCaption.Dock = DockStyle.Fill;
            _newPrefix.Dock = DockStyle.Fill;
            _newPrefix.Margin = new Padding(0, 6, 10, 6);

            var addButton = Theme.CreatePrimaryButton("＋ 增加");
            addButton.Dock = DockStyle.Fill;
            addButton.Margin = new Padding(0, 5, 10, 5);
            addButton.Click += delegate { AddTypedPrefix(); };
            _newPrefix.KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.KeyCode == Keys.Enter)
                {
                    AddTypedPrefix();
                    args.Handled = true;
                    args.SuppressKeyPress = true;
                }
            };

            addRow.Controls.Add(addCaption, 0, 0);
            addRow.Controls.Add(_newPrefix, 1, 0);
            addRow.Controls.Add(addButton, 2, 0);
            layout.Controls.Add(addRow, 0, 1);

            var currentCaption = Theme.CreateLabel(
                "当前一级字段与说明（勾选“选项卡”后才显示快捷按钮）", Theme.Body, Theme.Text);
            currentCaption.Dock = DockStyle.Fill;
            currentCaption.TextAlign = ContentAlignment.BottomLeft;
            currentCaption.Margin = new Padding(0, 4, 0, 3);
            layout.Controls.Add(currentCaption, 0, 2);
            _usedPrefixPanel.Dock = DockStyle.Top;
            _usedPrefixPanel.AutoSize = true;
            _usedPrefixPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _usedPrefixPanel.AutoScroll = false;
            _usedPrefixPanel.ColumnCount = 1;
            _usedPrefixPanel.RowCount = 0;
            _usedPrefixPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _usedPrefixPanel.BackColor = Theme.Surface;
            _usedPrefixPanel.Margin = new Padding(0, 0, 0, 4);
            _usedPrefixPanel.Padding = new Padding(0, 2, 0, 2);
            layout.Controls.Add(_usedPrefixPanel, 0, 3);

            var middleTitle = Theme.CreateLabel(
                "标准件二级字段管理（名称，例如：接近开关、磁吸开关）",
                Theme.BodyBold, Theme.Text);
            middleTitle.Dock = DockStyle.Fill;
            middleTitle.TextAlign = ContentAlignment.BottomLeft;
            middleTitle.Margin = new Padding(0, 6, 0, 4);
            layout.Controls.Add(middleTitle, 0, 4);

            var addMiddleRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = Theme.Surface
            };
            addMiddleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,
                TextWidth("增加二级字段", Theme.Body, 18)));
            addMiddleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260f));
            addMiddleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));
            addMiddleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var middleCaption = Theme.CreateFieldLabel("增加二级字段");
            middleCaption.Dock = DockStyle.Fill;
            _newMiddleName.Dock = DockStyle.Fill;
            _newMiddleName.Margin = new Padding(0, 6, 10, 6);
            var addMiddleButton = Theme.CreatePrimaryButton("＋ 增加");
            addMiddleButton.Dock = DockStyle.Fill;
            addMiddleButton.Margin = new Padding(0, 5, 10, 5);
            addMiddleButton.Click += delegate { AddTypedMiddleName(); };
            _newMiddleName.KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.KeyCode == Keys.Enter)
                {
                    AddTypedMiddleName();
                    args.Handled = true;
                    args.SuppressKeyPress = true;
                }
            };
            var middleHint = Theme.CreateValueLabel(
                "立即显示在快捷区下排；格式：一级-二级-三级型号");
            middleHint.Dock = DockStyle.Fill;
            middleHint.Font = Theme.Small;
            middleHint.ForeColor = Theme.Muted;
            addMiddleRow.Controls.Add(middleCaption, 0, 0);
            addMiddleRow.Controls.Add(_newMiddleName, 1, 0);
            addMiddleRow.Controls.Add(addMiddleButton, 2, 0);
            addMiddleRow.Controls.Add(middleHint, 3, 0);
            layout.Controls.Add(addMiddleRow, 0, 5);

            _usedMiddleNamePanel.Dock = DockStyle.Top;
            _usedMiddleNamePanel.AutoSize = true;
            _usedMiddleNamePanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _usedMiddleNamePanel.AutoScroll = false;
            _usedMiddleNamePanel.ColumnCount = 1;
            _usedMiddleNamePanel.RowCount = 0;
            _usedMiddleNamePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _usedMiddleNamePanel.BackColor = Theme.Surface;
            _usedMiddleNamePanel.Margin = new Padding(0, 0, 0, 4);
            _usedMiddleNamePanel.Padding = new Padding(0, 2, 0, 2);
            layout.Controls.Add(_usedMiddleNamePanel, 0, 6);

            viewport.Controls.Add(layout);
            return viewport;
        }

        private Control BuildField(string caption, Control editor, string hint)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Theme.Surface
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,
                Math.Max(96, TextWidth(caption, Theme.Body, 18))));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var label = Theme.CreateFieldLabel(caption);
            label.Dock = DockStyle.Fill;
            label.ForeColor = Theme.Text;

            editor.Dock = DockStyle.Fill;
            editor.Margin = new Padding(0, 5, 10, 5);

            var hintLabel = Theme.CreateValueLabel(hint);
            hintLabel.Font = Theme.Small;
            hintLabel.ForeColor = Theme.Muted;
            hintLabel.Dock = DockStyle.Fill;

            layout.Controls.Add(label, 0, 0);
            layout.Controls.Add(editor, 1, 0);
            layout.Controls.Add(hintLabel, 2, 0);
            return layout;
        }

        /// <summary>标准件前缀列表变化后重建下方的前缀行。</summary>
        private void WireNamingEditors()
        {
            _prefixes.TextChanged += delegate
            {
                if (!_syncing)
                {
                    UpdatePrefixButtons();
                }
            };
            _middleNames.TextChanged += delegate
            {
                if (!_syncing)
                {
                    UpdateMiddleNameButtons();
                }
            };
        }

        /// <summary>按当前前缀列表重建纵向“前缀标签 + 说明 + 删除”行。</summary>
        private void UpdatePrefixButtons()
        {
            if (_syncing)
            {
                return;
            }

            _syncing = true;
            try
            {
                _usedPrefixPanel.SuspendLayout();
                foreach (Control control in new List<Control>(GetControls(_usedPrefixPanel)))
                {
                    _usedPrefixPanel.Controls.Remove(control);
                    control.Dispose();
                }
                _usedPrefixPanel.RowStyles.Clear();
                _usedPrefixPanel.RowCount = 0;

                var prefixes = NamingOptionsFactory.ParsePrefixes(_prefixes.Text);
                if (prefixes.Length == 0)
                {
                    _usedPrefixPanel.RowCount = 1;
                    _usedPrefixPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
                    var empty = Theme.CreateLabel("（还没有前缀，请在上方输入前缀和说明后点击“增加”）", Theme.Small, Theme.Muted);
                    empty.Dock = DockStyle.Fill;
                    _usedPrefixPanel.Controls.Add(empty, 0, 0);
                }

                for (var index = 0; index < prefixes.Length; index++)
                {
                    var prefix = prefixes[index];
                    var value = prefix;
                    var row = new TableLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        ColumnCount = 4,
                        RowCount = 1,
                        BackColor = Theme.Surface,
                        Margin = new Padding(0, 0, 0, 6)
                    };
                    row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132f));
                    row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                    row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,
                        Math.Max(100, TextWidth("选项卡", Theme.Body, 52))));
                    row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42f));

                    var tag = Theme.CreateValueLabel(value);
                    tag.Dock = DockStyle.Fill;
                    tag.TextAlign = ContentAlignment.MiddleCenter;
                    tag.BackColor = Color.FromArgb(232, 244, 255);
                    tag.ForeColor = Theme.Accent;
                    tag.BorderStyle = BorderStyle.FixedSingle;
                    tag.Margin = new Padding(0, 3, 8, 3);

                    string savedDescription;
                    _prefixDescriptions.TryGetValue(value, out savedDescription);
                    var description = Theme.CreateTextBox();
                    description.Text = savedDescription ?? string.Empty;
                    description.Dock = DockStyle.Fill;
                    description.Margin = new Padding(0, 3, 8, 3);
                    description.TextChanged += delegate
                    {
                        _prefixDescriptions[value] = description.Text;
                    };

                    var showOnTab = new CheckBox
                    {
                        Text = "选项卡",
                        Checked = _standardTabPrefixes.Contains(value),
                        AutoSize = true,
                        Anchor = AnchorStyles.None,
                        ForeColor = Theme.Text
                    };
                    showOnTab.CheckedChanged += delegate
                    {
                        if (showOnTab.Checked)
                            _standardTabPrefixes.Add(value);
                        else
                            _standardTabPrefixes.Remove(value);
                    };

                    var delete = Theme.CreateSecondaryButton("×");
                    delete.Dock = DockStyle.Fill;
                    delete.Margin = new Padding(0, 3, 0, 3);
                    delete.Click += delegate { RemovePrefix(value); };

                    row.Controls.Add(tag, 0, 0);
                    row.Controls.Add(description, 1, 0);
                    row.Controls.Add(showOnTab, 2, 0);
                    row.Controls.Add(delete, 3, 0);

                    _usedPrefixPanel.RowCount = index + 1;
                    _usedPrefixPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
                    _usedPrefixPanel.Controls.Add(row, 0, index);
                }

                _usedPrefixPanel.RowCount = Math.Max(1, prefixes.Length);
            }
            finally
            {
                _usedPrefixPanel.ResumeLayout();
                _syncing = false;
            }
        }

        private static IEnumerable<Control> GetControls(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
            }
        }

        /// <summary>
        /// 按当前字体测量文字宽度（再加留白）。固定像素在 125% / 150% 缩放下会把
        /// 中文字裁掉，所以列宽都用字体测量值来定。
        /// </summary>
        private static int TextWidth(string text, Font font, int padding)
        {
            var measured = TextRenderer.MeasureText(text ?? string.Empty, font ?? Theme.Body).Width;
            return measured + padding;
        }

        /// <summary>灰色说明文字：自动换行，行高随内容增长，不会被下一行压住。</summary>
        private static Label CreateWrappedHint(string text)
        {
            var label = Theme.CreateLabel(text, Theme.Small, Theme.Muted);
            label.AutoSize = true;
            label.MaximumSize = new Size(820, 0);
            label.Margin = new Padding(0, 2, 0, 2);
            return label;
        }

        /// <summary>按当前配置重建纵向“中文中间名 + 删除”行。</summary>
        private void UpdateMiddleNameButtons()
        {
            if (_syncing)
            {
                return;
            }

            _syncing = true;
            try
            {
                _usedMiddleNamePanel.SuspendLayout();
                foreach (Control control in new List<Control>(GetControls(_usedMiddleNamePanel)))
                {
                    _usedMiddleNamePanel.Controls.Remove(control);
                    control.Dispose();
                }
                _usedMiddleNamePanel.RowStyles.Clear();
                _usedMiddleNamePanel.RowCount = 0;

                var names = NamingOptionsFactory.ParsePrefixes(_middleNames.Text);
                if (names.Length == 0)
                {
                    _usedMiddleNamePanel.RowCount = 1;
                    _usedMiddleNamePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
                    var empty = Theme.CreateLabel("（还没有中间名，请在上方输入后点击“增加”）", Theme.Small, Theme.Muted);
                    empty.Dock = DockStyle.Fill;
                    _usedMiddleNamePanel.Controls.Add(empty, 0, 0);
                }

                for (var index = 0; index < names.Length; index++)
                {
                    var value = names[index];
                    var row = new TableLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        ColumnCount = 4,
                        RowCount = 1,
                        BackColor = Theme.Surface,
                        Margin = new Padding(0, 0, 0, 6)
                    };
                    row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160f));
                    row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                    row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,
                        Math.Max(100, TextWidth("选项卡", Theme.Body, 52))));
                    row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42f));

                    var tag = Theme.CreateValueLabel(value);
                    tag.Dock = DockStyle.Fill;
                    tag.TextAlign = ContentAlignment.MiddleLeft;
                    tag.BackColor = Color.FromArgb(245, 248, 251);
                    tag.BorderStyle = BorderStyle.FixedSingle;
                    tag.Padding = new Padding(10, 0, 0, 0);
                    tag.Margin = new Padding(0, 3, 8, 3);

                    // 备注：例如“传动 = 直线导轨和直线模组和滚珠丝杆等”，只作记录用。
                    string savedDescription;
                    _middleNameDescriptions.TryGetValue(value, out savedDescription);
                    var description = Theme.CreateTextBox();
                    description.Text = savedDescription ?? string.Empty;
                    description.Dock = DockStyle.Fill;
                    description.Margin = new Padding(0, 3, 8, 3);
                    description.TextChanged += delegate
                    {
                        _middleNameDescriptions[value] = description.Text;
                    };

                    var showOnTab = new CheckBox
                    {
                        Text = "选项卡",
                        Checked = _standardTabMiddleNames.Contains(value),
                        AutoSize = true,
                        Anchor = AnchorStyles.None,
                        ForeColor = Theme.Text
                    };
                    showOnTab.CheckedChanged += delegate
                    {
                        if (showOnTab.Checked)
                            _standardTabMiddleNames.Add(value);
                        else
                            _standardTabMiddleNames.Remove(value);
                    };

                    var delete = Theme.CreateSecondaryButton("×");
                    delete.Dock = DockStyle.Fill;
                    delete.Margin = new Padding(0, 3, 0, 3);
                    delete.Click += delegate { RemoveMiddleName(value); };
                    row.Controls.Add(tag, 0, 0);
                    row.Controls.Add(description, 1, 0);
                    row.Controls.Add(showOnTab, 2, 0);
                    row.Controls.Add(delete, 3, 0);

                    _usedMiddleNamePanel.RowCount = index + 1;
                    _usedMiddleNamePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
                    _usedMiddleNamePanel.Controls.Add(row, 0, index);
                }

                _usedMiddleNamePanel.RowCount = Math.Max(1, names.Length);
            }
            finally
            {
                _usedMiddleNamePanel.ResumeLayout();
                _syncing = false;
            }
        }

        private void AddTypedMiddleName()
        {
            var value = _newMiddleName.Text.Trim().Trim('_', '-');
            if (value.Length == 0)
            {
                return;
            }

            var names = new List<string>(NamingOptionsFactory.ParsePrefixes(_middleNames.Text));
            if (names.Count >= AddinConstants.MaxMiddleNameCommands && !names.Contains(value))
            {
                MessageBox.Show(this, "选项卡面板最多显示 12 个中间名，请先删除一个再增加。",
                    AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!names.Contains(value))
            {
                names.Add(value);
                _standardTabMiddleNames.Add(value);
                if (!_middleNameDescriptions.ContainsKey(value))
                {
                    _middleNameDescriptions[value] = string.Empty;
                }
            }
            _middleNames.Text = NamingOptionsFactory.SerializePrefixes(names);
            _newMiddleName.Clear();
            _newMiddleName.Focus();
        }

        private void RemoveMiddleName(string value)
        {
            var names = new List<string>(NamingOptionsFactory.ParsePrefixes(_middleNames.Text));
            names.Remove(value);
            _standardTabMiddleNames.Remove(value);
            _middleNameDescriptions.Remove(value);
            _middleNames.Text = NamingOptionsFactory.SerializePrefixes(names);
        }

        private void AddPrefix(string prefix, string description)
        {
            var prefixes = new List<string>(NamingOptionsFactory.ParsePrefixes(_prefixes.Text));
            if (!prefixes.Contains(prefix))
            {
                prefixes.Add(prefix);
                _standardTabPrefixes.Add(prefix);
            }

            _prefixDescriptions[prefix] = (description ?? string.Empty).Trim();

            _prefixes.Text = NamingOptionsFactory.SerializePrefixes(prefixes);
        }

        private void AddTypedPrefix()
        {
            var prefix = _newPrefix.Text.Trim().TrimEnd('_', '-', '*', '＊');
            if (prefix.Length == 0)
            {
                return;
            }

            var existing = NamingOptionsFactory.ParsePrefixes(_prefixes.Text);
            if (existing.Length >= AddinConstants.MaxPrefixCommands &&
                Array.IndexOf(existing, prefix) < 0)
            {
                MessageBox.Show(this, "选项卡面板最多显示 12 个前缀，请先删除一个再增加。",
                    AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 说明改在下方每一行里填写，新增时先留空。
            AddPrefix(prefix, string.Empty);
            _newPrefix.Clear();
            _newPrefix.Focus();
        }

        private void RemovePrefix(string prefix)
        {
            var prefixes = new List<string>(NamingOptionsFactory.ParsePrefixes(_prefixes.Text));
            prefixes.Remove(prefix);
            _prefixDescriptions.Remove(prefix);
            _standardTabPrefixes.Remove(prefix);
            _prefixes.Text = NamingOptionsFactory.SerializePrefixes(prefixes);
        }

        private void LoadFromSettings()
        {
            var s = _host.Settings;
            _separator.Text = "-（兼容 _）";
            _machinedSegments.Clear();
            _machinedSegments.AddRange(NamingOptionsFactory.ParseMachinedSegments(s.MachinedSegments));
            _segmentLabels.Clear();
            _segmentLabels.AddRange(NamingOptionsFactory.ParseMachinedSegmentLabels(
                s.MachinedSegmentLabels, _machinedSegments.Count));
            _segmentBomNameFlags.Clear();
            _segmentBomNameFlags.AddRange(NamingOptionsFactory.ParseMachinedSegmentBomNameFlags(
                s.MachinedSegmentBomNameFlags, _machinedSegments));
            RebuildSegmentRows();
            _prefixDescriptions.Clear();
            foreach (var pair in NamingOptionsFactory.ParsePrefixDescriptions(s.BomPrefixDescriptions))
            {
                _prefixDescriptions[pair.Key] = pair.Value;
            }
            if (!_prefixDescriptions.ContainsKey("淘宝"))
                _prefixDescriptions["淘宝"] = "淘宝或其他平台采购件";
            if (!_prefixDescriptions.ContainsKey("代理"))
                _prefixDescriptions["代理"] = "代理商或正规渠道采购件";
            if (!_prefixDescriptions.ContainsKey("淘宝追加工"))
                _prefixDescriptions["淘宝追加工"] = "淘宝采购后需要追加加工";
            _prefixes.Text = NamingOptionsFactory.SerializePrefixes(
                NamingOptionsFactory.ParsePrefixes(s.BomPrefixes));
            _middleNames.Text = NamingOptionsFactory.SerializePrefixes(
                NamingOptionsFactory.ParsePrefixes(s.BomMiddleNames));
            _middleNameDescriptions.Clear();
            foreach (var pair in NamingOptionsFactory.ParsePrefixDescriptions(s.BomMiddleNameDescriptions))
            {
                _middleNameDescriptions[pair.Key] = pair.Value;
            }
            _standardTabPrefixes.Clear();
            foreach (var value in NamingOptionsFactory.ParsePrefixes(s.StandardTabPrefixes))
                _standardTabPrefixes.Add(value);
            _standardTabMiddleNames.Clear();
            foreach (var value in NamingOptionsFactory.ParsePrefixes(s.StandardTabMiddleNames))
                _standardTabMiddleNames.Add(value);
            _machinedMaterialProcessRules.Text = (s.MachinedMaterialProcessRules ?? string.Empty)
                .Replace("\r\n", "\n").Replace("\n", "\r\n");
            _machinedTabLevel2Values.Clear();
            foreach (var value in NamingOptionsFactory.ParsePrefixes(s.MachinedTabLevel2Values))
                _machinedTabLevel2Values.Add(value);
            _machinedTabLevel3Values.Clear();
            foreach (var value in NamingOptionsFactory.ParsePrefixes(s.MachinedTabLevel3Values))
                _machinedTabLevel3Values.Add(value);
            _machinedLevel2Values.Text = NamingOptionsFactory.SerializePrefixes(
                NamingOptionsFactory.ParsePrefixes(s.MachinedLevel2Values));
            _machinedLevel3Values.Text = NamingOptionsFactory.SerializePrefixes(
                NamingOptionsFactory.ParsePrefixes(s.MachinedLevel3Values));
            _standardBindingEnabled.Checked = s.StandardPrefixBindingEnabled;
            _standardBindings.Text = s.StandardPrefixBindings;
            _partNumberSource.SelectedIndex = Math.Max(0, Math.Min(2, s.PartNumberSource));
            _cutRule.SelectedIndex = Math.Max(0, Math.Min(4, s.PartNumberCutRule));
            _pattern.Text = s.PartNumberPattern;

            // 材料段列表与解析表都读完之后，才能把两边的数据合到同一张表里。
            RebuildMachinedLevel2Rows();
        }

        private void ResetToDefault()
        {
            _separator.Text = "-（兼容 _）";
            _machinedSegments.Clear();
            _segmentLabels.Clear();
            _segmentBomNameFlags.Clear();
            _machinedSegments.AddRange(new[]
            {
                MachinedSegmentKind.Date,
                MachinedSegmentKind.Material,
                MachinedSegmentKind.Name,
                MachinedSegmentKind.Serial,
                MachinedSegmentKind.Custom
            });
            _segmentLabels.AddRange(new[] { string.Empty, string.Empty, string.Empty, string.Empty, "安装说明" });
            _segmentBomNameFlags.AddRange(new[] { false, false, true, true, true });
            RebuildSegmentRows();
            _prefixDescriptions.Clear();
            _prefixDescriptions["淘宝"] = "淘宝或其他平台采购件";
            _prefixDescriptions["代理"] = "代理商或正规渠道采购件";
            _prefixDescriptions["淘宝追加工"] = "淘宝采购后需要追加加工";
            _standardTabPrefixes.Clear();
            _standardTabPrefixes.UnionWith(new[] { "淘宝", "代理", "淘宝追加工" });
            _standardTabMiddleNames.Clear();
            _standardTabMiddleNames.UnionWith(new[] { "接近开关", "直线导轨", "电机", "丝杆" });
            _prefixes.Text = "淘宝 代理 淘宝追加工";
            _middleNames.Text = "接近开关 直线导轨 电机 丝杆";
            _middleNameDescriptions.Clear();
            _machinedMaterialProcessRules.Text =
                "6061=6061|cnc|本色氧化\r\n" +
                "5052=5052|钣金|白色细砂纹烤漆\r\n" +
                "304=304|cnc|\r\n轴304=304|车铣|\r\n淘宝=-|追加工|";
            _machinedTabLevel2Values.Clear();
            _machinedTabLevel2Values.UnionWith(new[] { "6061", "5052", "304", "轴304" });
            _machinedTabLevel3Values.Clear();
            _machinedLevel2Values.Text = "6061 5052 304 轴304";
            _machinedLevel3Values.Text = string.Empty;
            RebuildMachinedLevel2Rows();
            _standardBindingEnabled.Checked = true;
            _standardBindings.Text = "电机=代理|接近开关=代理|直线导轨=代理";
            _partNumberSource.SelectedIndex = 0;
            _cutRule.SelectedIndex = 0;
            _pattern.Text = string.Empty;
            UpdatePrefixButtons();
        }

        private void Save()
        {
            if (!ValidateSegmentLayout())
            {
                return;
            }

            var s = _host.Settings;
            s.SegmentSeparator = "-_";
            s.MachinedSegments = NamingOptionsFactory.SerializeMachinedSegments(_machinedSegments);
            s.MachinedSegmentLabels = NamingOptionsFactory.SerializeMachinedSegmentLabels(_segmentLabels);
            s.MachinedSegmentBomNameFlags =
                NamingOptionsFactory.SerializeMachinedSegmentBomNameFlags(_segmentBomNameFlags);
            s.MaterialSegment = SegmentPosition(MachinedSegmentKind.Material);
            s.NameSegment = SegmentPosition(MachinedSegmentKind.Name);
            s.BomPrefixes = NamingOptionsFactory.SerializePrefixes(
                NamingOptionsFactory.ParsePrefixes(_prefixes.Text));
            s.BomPrefixDescriptions = NamingOptionsFactory.SerializePrefixDescriptions(
                NamingOptionsFactory.ParsePrefixes(_prefixes.Text), _prefixDescriptions);
            s.BomMiddleNames = NamingOptionsFactory.SerializePrefixes(
                NamingOptionsFactory.ParsePrefixes(_middleNames.Text));
            s.BomMiddleNameDescriptions = NamingOptionsFactory.SerializePrefixDescriptions(
                NamingOptionsFactory.ParsePrefixes(_middleNames.Text), _middleNameDescriptions);
            s.StandardTabPrefixes = SerializeSelectedValues(
                NamingOptionsFactory.ParsePrefixes(_prefixes.Text), _standardTabPrefixes);
            s.StandardTabMiddleNames = SerializeSelectedValues(
                NamingOptionsFactory.ParsePrefixes(_middleNames.Text), _standardTabMiddleNames);
            s.MachinedMaterialProcessRules = NamingOptionsFactory.NormalizeMaterialProcessPresetFormat(
                _machinedMaterialProcessRules.Text.Trim());
            s.MachinedLevel2Values = NamingOptionsFactory.SerializePrefixes(
                NamingOptionsFactory.ParsePrefixes(_machinedLevel2Values.Text));
            s.MachinedLevel3Values = NamingOptionsFactory.SerializePrefixes(
                NamingOptionsFactory.ParsePrefixes(_machinedLevel3Values.Text));
            s.MachinedTabLevel2Values = SerializeSelectedValues(
                NamingOptionsFactory.ParsePrefixes(_machinedLevel2Values.Text),
                _machinedTabLevel2Values);
            s.MachinedTabLevel3Values = SerializeSelectedValues(
                NamingOptionsFactory.ParsePrefixes(_machinedLevel3Values.Text),
                _machinedTabLevel3Values);
            s.StandardPrefixBindingEnabled = _standardBindingEnabled.Checked;
            s.StandardPrefixBindings = _standardBindings.Text.Trim();
            s.NamingPresetVersion = 14;
            s.PartNumberSource = _partNumberSource.SelectedIndex;
            s.PartNumberCutRule = _cutRule.SelectedIndex;
            s.PartNumberPattern = _pattern.Text.Trim();
            s.UseNameSegments = true;
            s.Save();

            // 立刻按最新设置重建 MechKit 选项卡上的快捷按钮：新增字段、勾选或取消
            // “选项卡”都会立即生效，不需要重启 SOLIDWORKS。
            _host.RefreshNamingCommands();

            Log.Info("命名规则已保存：加工件=" +
                NamingOptionsFactory.DescribeMachinedSegments(_machinedSegments, _segmentLabels) +
                "；标准件前缀=" + s.BomPrefixes + "；中间名=" + s.BomMiddleNames);
            Close();
        }

        private static string SerializeSelectedValues(IEnumerable<string> orderedValues,
            HashSet<string> selectedValues)
        {
            var selected = new List<string>();
            foreach (var value in orderedValues)
            {
                if (selectedValues.Contains(value))
                    selected.Add(value);
            }
            return NamingOptionsFactory.SerializePrefixes(selected);
        }

        private bool ValidateSegmentLayout()
        {
            if (_machinedSegments.Count == 0 || _machinedSegments[0] != MachinedSegmentKind.Date)
            {
                MessageBox.Show(this, "时间段必须固定在第 1 段。", AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            foreach (var kind in new[]
            {
                MachinedSegmentKind.Material,
                MachinedSegmentKind.Name,
                MachinedSegmentKind.Serial,
                MachinedSegmentKind.Extension
            })
            {
                var count = 0;
                foreach (var segment in _machinedSegments)
                {
                    if (segment == kind)
                    {
                        count++;
                    }
                }

                if (count > 1)
                {
                    MessageBox.Show(this,
                        SegmentDisplayName(kind) +
                        "只能设置一个；额外的占位请使用“自定义段”。",
                        AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }

            foreach (var line in (_machinedMaterialProcessRules.Text ?? string.Empty)
                .Replace("\r", string.Empty).Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                var equal = line.IndexOf('=');
                var payload = equal < 0 ? string.Empty : line.Substring(equal + 1);
                if (equal > 0 && payload.IndexOf('|') >= 0)
                {
                    // 表格格式：材料段=材料|工艺|表面处理，允许某一段留空。
                    continue;
                }
                var dash = payload.LastIndexOf('-');
                if (equal <= 0 || line.IndexOf(',') >= 0 || line.IndexOf('，') >= 0 ||
                    dash <= 0 || dash >= payload.Length - 1)
                {
                    MessageBox.Show(this,
                        "材料/工艺/表面处理预设格式不正确：\r\n" + line +
                        "\r\n\r\n正确示例：6061=6061|cnc|本色氧化",
                        AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }

            if (_standardBindingEnabled.Checked)
            {
                var prefixes = NamingOptionsFactory.ParsePrefixes(_prefixes.Text);
                var middleNames = NamingOptionsFactory.ParsePrefixes(_middleNames.Text);
                foreach (var pair in NamingOptionsFactory.ParsePrefixBindings(_standardBindings.Text))
                {
                    if (Array.IndexOf(middleNames, pair.Key) < 0 || Array.IndexOf(prefixes, pair.Value) < 0)
                    {
                        MessageBox.Show(this,
                            "绑定规则中的中间名和前缀必须先加入上方列表：\r\n" +
                            pair.Key + " → " + pair.Value,
                            AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                }
            }

            return true;
        }

        private static string SegmentDisplayName(MachinedSegmentKind kind)
        {
            switch (kind)
            {
                case MachinedSegmentKind.Material: return "材料段";
                case MachinedSegmentKind.Name: return "零件名称段";
                case MachinedSegmentKind.Serial: return "版本号段";
                case MachinedSegmentKind.Extension: return "拓展代号段";
                default: return "该分段";
            }
        }

        private int SegmentPosition(MachinedSegmentKind kind)
        {
            for (var index = 0; index < _machinedSegments.Count; index++)
            {
                if (_machinedSegments[index] == kind)
                {
                    return index + 1;
                }
            }

            return 0;
        }

    }
}
