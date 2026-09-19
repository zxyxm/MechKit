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
    ///   · 标准件：只维护前缀的增加与删除；实际套用前缀统一在选项卡/任务面板完成
    /// 底部是实时预览：输入一个零件名，立刻看到解析结果与是否进 BOM。
    /// </summary>
    internal sealed class NamingRuleForm : Form
    {
        private readonly IAddinHost _host;

        // 加工件栏
        private readonly TextBox _separator;
        private readonly TableLayoutPanel _segmentPanel;
        private readonly List<MachinedSegmentKind> _machinedSegments;
        private readonly ComboBox _partNumberSource;
        private readonly ComboBox _cutRule;
        private readonly TextBox _pattern;

        // 标准件栏
        private readonly TextBox _prefixes;
        private readonly TextBox _newPrefix;
        private readonly FlowLayoutPanel _usedPrefixPanel;

        // 预览
        private readonly TextBox _sample;
        private readonly Label _preview;

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
            _segmentPanel = new TableLayoutPanel();
            _machinedSegments = new List<MachinedSegmentKind>();
            _partNumberSource = new ComboBox();
            _cutRule = new ComboBox();
            _pattern = Theme.CreateTextBox();
            _prefixes = Theme.CreateTextBox();
            _newPrefix = Theme.CreateTextBox();
            _usedPrefixPanel = new FlowLayoutPanel();
            _sample = Theme.CreateTextBox();
            _preview = Theme.CreateValueLabel(string.Empty);

            BuildLayout();
            LoadFromSettings();
            UpdatePrefixButtons();
            UpdatePreview();
        }

        private void BuildLayout()
        {
            Text = "命名规则设置 - MechKit";
            Font = Theme.Body;
            BackColor = Theme.Canvas;
            WindowLayout.Attach(this, _host.Settings, new Size(940, 760), new Size(860, 680));

            var header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Theme.Accent };
            var title = Theme.CreateLabel("命名规则设置", Theme.Title, Color.White);
            title.Location = new Point(14, 9);
            var subtitle = Theme.CreateLabel("加工件用日期开头，标准件用前缀开头，各段一律用下划线分隔",
                Theme.Small, Color.FromArgb(214, 232, 248));
            subtitle.Location = new Point(15, 32);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 6), BackColor = Theme.Canvas };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Theme.Canvas
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 68f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 32f));

            var tabs = (TabControl)BuildTabs();
            var previewPanel = BuildPreviewPanel();
            layout.Controls.Add(tabs, 0, 0);
            layout.Controls.Add(previewPanel, 0, 1);

            Action updatePageLayout = delegate
            {
                var showPreview = tabs.SelectedIndex == 0;
                previewPanel.Visible = showPreview;
                layout.RowStyles[0].Height = showPreview ? 68f : 100f;
                layout.RowStyles[1].Height = showPreview ? 32f : 0f;
            };
            tabs.SelectedIndexChanged += delegate { updatePageLayout(); };
            updatePageLayout();
            body.Controls.Add(layout);

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
                RowCount = 7,
                BackColor = Theme.Surface,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0, 0, 6, 8)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));

            var title = Theme.CreateLabel("加工件命名规则（按段顺序进入 BOM）", Theme.BodyBold, Theme.Text);
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(BuildField("分段符号", _separator,
                "各段之间的分隔符，默认下划线 _"), 0, 1);

            var segmentCaption = Theme.CreateLabel(
                "加工件分段（使用 ↑ / ↓ 调整前后顺序）", Theme.Body, Theme.Text);
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

            _partNumberSource.DropDownStyle = ComboBoxStyle.DropDownList;
            _partNumberSource.Font = Theme.Body;
            _partNumberSource.Items.AddRange(new object[]
            {
                "用零件文件名（推荐）",
                "优先读自定义属性「图号/零件号」",
                "只用自定义属性"
            });
            layout.Controls.Add(BuildField("图号来源", _partNumberSource,
                "图号取文件名，还是零件自定义属性里的图号"), 0, 5);

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
            layout.Controls.Add(BuildField("图号截断", _cutRule,
                "图号需要截断时选一项；一般保持「完整文件名」"), 0, 6);

            viewport.Controls.Add(layout);
            return viewport;
        }

        private void RebuildSegmentRows()
        {
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

            UpdatePreview();
        }

        private Control BuildSegmentRow(int index)
        {
            var kind = _machinedSegments[index];
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 1,
                BackColor = index % 2 == 0 ? Color.FromArgb(248, 249, 250) : Theme.Surface,
                Margin = new Padding(0, 1, 0, 1),
                Padding = new Padding(4, 3, 4, 3)
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
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
                    "材料段", "零件名称段", "扩展序号段", "自定义段"
                });
                selector.SelectedIndex = SegmentSelectorIndex(kind);
                var capturedIndex = index;
                selector.SelectedIndexChanged += delegate
                {
                    if (selector.SelectedIndex < 0 || capturedIndex >= _machinedSegments.Count)
                    {
                        return;
                    }

                    _machinedSegments[capturedIndex] = SegmentKindFromSelector(selector.SelectedIndex);
                    RebuildSegmentRows();
                };
            }

            var hint = Theme.CreateValueLabel(SegmentHint(kind));
            hint.Dock = DockStyle.Fill;
            hint.Font = Theme.Small;
            hint.ForeColor = Theme.Muted;
            hint.Padding = new Padding(8, 0, 4, 0);

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
            row.Controls.Add(up, 3, 0);
            row.Controls.Add(down, 4, 0);
            row.Controls.Add(remove, 5, 0);
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
            _machinedSegments.RemoveAt(index);
            _machinedSegments.Insert(target, item);
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
            RebuildSegmentRows();
        }

        private static int SegmentSelectorIndex(MachinedSegmentKind kind)
        {
            switch (kind)
            {
                case MachinedSegmentKind.Name: return 1;
                case MachinedSegmentKind.Serial: return 2;
                case MachinedSegmentKind.Custom: return 3;
                default: return 0;
            }
        }

        private static MachinedSegmentKind SegmentKindFromSelector(int index)
        {
            switch (index)
            {
                case 1: return MachinedSegmentKind.Name;
                case 2: return MachinedSegmentKind.Serial;
                case 3: return MachinedSegmentKind.Custom;
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
                case MachinedSegmentKind.Serial: return "扩展序号或版本号，可选";
                default: return "自定义占位段，不参与字段解析";
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
                RowCount = 4,
                BackColor = Theme.Surface,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0, 0, 6, 8)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 106f));

            var title = Theme.CreateLabel("标准件前缀管理", Theme.BodyBold, Theme.Text);
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
            addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
            addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300f));
            addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));
            addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var addCaption = Theme.CreateFieldLabel("增加前缀");
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

            var addHint = Theme.CreateValueLabel("保存后会成为选项卡面板上的快捷按钮");
            addHint.Dock = DockStyle.Fill;
            addHint.Font = Theme.Small;
            addHint.ForeColor = Theme.Muted;
            addRow.Controls.Add(addCaption, 0, 0);
            addRow.Controls.Add(_newPrefix, 1, 0);
            addRow.Controls.Add(addButton, 2, 0);
            addRow.Controls.Add(addHint, 3, 0);
            layout.Controls.Add(addRow, 0, 1);

            var currentCaption = Theme.CreateLabel("当前前缀（点 × 删除）", Theme.Body, Theme.Text);
            currentCaption.Dock = DockStyle.Fill;
            currentCaption.TextAlign = ContentAlignment.BottomLeft;
            currentCaption.Margin = new Padding(0, 4, 0, 3);
            layout.Controls.Add(currentCaption, 0, 2);
            _usedPrefixPanel.Dock = DockStyle.Fill;
            _usedPrefixPanel.AutoScroll = true;
            _usedPrefixPanel.WrapContents = true;
            _usedPrefixPanel.BackColor = Theme.Surface;
            _usedPrefixPanel.Margin = new Padding(0, 0, 0, 4);
            _usedPrefixPanel.Padding = new Padding(0, 2, 0, 2);
            layout.Controls.Add(_usedPrefixPanel, 0, 3);

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
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
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

        private Control BuildPreviewPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(12, 10, 12, 10) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Theme.Surface
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            layout.Controls.Add(Theme.CreateLabel("实时预览（改任意一项立刻更新）", Theme.BodyBold, Theme.Text), 0, 0);
            layout.Controls.Add(BuildField("试一个名字", _sample, "例如 20260908_6061_扫码枪安装板 或 电机_MG996"), 0, 1);

            _preview.Dock = DockStyle.Fill;
            _preview.Font = Theme.Mono;
            _preview.ForeColor = Theme.Text;
            _preview.Padding = new Padding(6, 4, 6, 4);
            _preview.BackColor = Color.FromArgb(250, 251, 252);
            layout.Controls.Add(_preview, 0, 2);

            panel.Controls.Add(layout);

            _sample.TextChanged += delegate { UpdatePreview(); };
            _separator.TextChanged += delegate { UpdatePreview(); };
            _pattern.TextChanged += delegate { UpdatePreview(); };
            _partNumberSource.SelectedIndexChanged += delegate { UpdatePreview(); };
            _cutRule.SelectedIndexChanged += delegate { UpdatePreview(); };
            _prefixes.TextChanged += delegate
            {
                if (!_syncing)
                {
                    UpdatePrefixButtons();
                }

                UpdatePreview();
            };

            return panel;
        }

        /// <summary>按当前前缀列表重建按钮：每个前缀一个「名字 ×」按钮，点一下删除。</summary>
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

                var prefixes = NamingOptionsFactory.ParsePrefixes(_prefixes.Text);
                if (prefixes.Length == 0)
                {
                    _usedPrefixPanel.Controls.Add(Theme.CreateLabel("（还没有前缀，请在上方输入后点击“增加”）", Theme.Small, Theme.Muted));
                }

                foreach (var prefix in prefixes)
                {
                    var value = prefix;
                    var button = Theme.CreateSecondaryButton(value + "  ×");
                    button.Width = 90;
                    button.Height = 26;
                    button.Margin = new Padding(0, 0, 6, 6);
                    button.Click += delegate { RemovePrefix(value); };
                    _usedPrefixPanel.Controls.Add(button);
                }
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

        private void AddPrefix(string prefix)
        {
            var prefixes = new List<string>(NamingOptionsFactory.ParsePrefixes(_prefixes.Text));
            if (!prefixes.Contains(prefix))
            {
                prefixes.Add(prefix);
            }

            _prefixes.Text = NamingOptionsFactory.SerializePrefixes(prefixes);
        }

        private void AddTypedPrefix()
        {
            var prefix = _newPrefix.Text.Trim().TrimEnd('_', '*', '＊');
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

            AddPrefix(prefix);
            _newPrefix.Clear();
            _newPrefix.Focus();
        }

        private void RemovePrefix(string prefix)
        {
            var prefixes = new List<string>(NamingOptionsFactory.ParsePrefixes(_prefixes.Text));
            prefixes.Remove(prefix);
            _prefixes.Text = NamingOptionsFactory.SerializePrefixes(prefixes);
        }

        private void LoadFromSettings()
        {
            var s = _host.Settings;
            _separator.Text = string.IsNullOrEmpty(s.SegmentSeparator) ? "_" : s.SegmentSeparator;
            _machinedSegments.Clear();
            _machinedSegments.AddRange(NamingOptionsFactory.ParseMachinedSegments(s.MachinedSegments));
            RebuildSegmentRows();
            _prefixes.Text = NamingOptionsFactory.SerializePrefixes(
                NamingOptionsFactory.ParsePrefixes(s.BomPrefixes));
            _partNumberSource.SelectedIndex = Math.Max(0, Math.Min(2, s.PartNumberSource));
            _cutRule.SelectedIndex = Math.Max(0, Math.Min(4, s.PartNumberCutRule));
            _pattern.Text = s.PartNumberPattern;
            _sample.Text = "20260908_6061_扫码枪安装板";
        }

        private void ResetToDefault()
        {
            _separator.Text = "_";
            _machinedSegments.Clear();
            _machinedSegments.AddRange(new[]
            {
                MachinedSegmentKind.Date,
                MachinedSegmentKind.Material,
                MachinedSegmentKind.Name,
                MachinedSegmentKind.Serial
            });
            RebuildSegmentRows();
            _prefixes.Text = "电机 电气 淘宝";
            _partNumberSource.SelectedIndex = 0;
            _cutRule.SelectedIndex = 0;
            _pattern.Text = string.Empty;
            UpdatePrefixButtons();
            UpdatePreview();
        }

        private void Save()
        {
            if (!ValidateSegmentLayout())
            {
                return;
            }

            var s = _host.Settings;
            s.SegmentSeparator = string.IsNullOrEmpty(_separator.Text) ? "_" : _separator.Text;
            s.MachinedSegments = NamingOptionsFactory.SerializeMachinedSegments(_machinedSegments);
            s.MaterialSegment = SegmentPosition(MachinedSegmentKind.Material);
            s.NameSegment = SegmentPosition(MachinedSegmentKind.Name);
            s.BomPrefixes = NamingOptionsFactory.SerializePrefixes(
                NamingOptionsFactory.ParsePrefixes(_prefixes.Text));
            s.PartNumberSource = _partNumberSource.SelectedIndex;
            s.PartNumberCutRule = _cutRule.SelectedIndex;
            s.PartNumberPattern = _pattern.Text.Trim();
            s.UseNameSegments = true;
            s.Save();

            Log.Info("命名规则已保存：加工件=" +
                NamingOptionsFactory.DescribeMachinedSegments(_machinedSegments) +
                "；标准件前缀=" + s.BomPrefixes);
            Close();
        }

        private bool ValidateSegmentLayout()
        {
            if (_machinedSegments.Count == 0 || _machinedSegments[0] != MachinedSegmentKind.Date)
            {
                MessageBox.Show(this, "时间段必须固定在第 1 段。", AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            foreach (var kind in new[] { MachinedSegmentKind.Material, MachinedSegmentKind.Name })
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
                        (kind == MachinedSegmentKind.Material ? "材料段" : "零件名称段") +
                        "只能设置一个；额外的占位请使用“自定义段”。",
                        AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }

            return true;
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

        /// <summary>用当前界面上的规则解析示例名称。</summary>
        private void UpdatePreview()
        {
            try
            {
                var naming = new NamingOptions
                {
                    Source = (PartNumberSource)Math.Max(0, _partNumberSource.SelectedIndex),
                    Cut = (FileNameCutRule)Math.Max(0, _cutRule.SelectedIndex),
                    Pattern = _pattern.Text.Trim(),
                    UseNameSegments = true,
                    SegmentSeparator = string.IsNullOrEmpty(_separator.Text) ? "_" : _separator.Text,
                    MaterialSegment = SegmentPosition(MachinedSegmentKind.Material),
                    NameSegment = SegmentPosition(MachinedSegmentKind.Name),
                    MachinedSegments = _machinedSegments.ToArray(),
                    BomPrefixes = NamingOptionsFactory.ParsePrefixes(_prefixes.Text),
                    RequireBomPattern = _host.Settings.BomRequirePattern
                };

                var sample = _sample.Text.Trim();
                if (sample.Length == 0)
                {
                    _preview.Text = "输入一个零件名试试…";
                    return;
                }

                Func<string, string> none = delegate { return string.Empty; };
                var fileName = NamingOptions.GetFileNameWithoutExtension(sample);

                var inBom = naming.MatchesBomPattern(sample)
                    ? "✔ 进 BOM（" + (naming.IsMachinedName(fileName) ? "加工件" : "标准件") + "）"
                    : "✘ 不进 BOM（视为子零件）";

                _preview.Text = string.Format(
                    "{0}\r\n\r\n图号：{1}\r\n名称：{2}\r\n材料：{3}",
                    inBom,
                    naming.ResolvePartNumber(sample, none),
                    naming.ResolveName(sample, none),
                    naming.ResolveMaterialFromSegments(sample));
            }
            catch (Exception ex)
            {
                _preview.Text = "预览失败：" + ex.Message;
            }
        }
    }
}
