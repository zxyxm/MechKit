using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MechKit.Core;

namespace MechKit.UI
{
    /// <summary>
    /// 命名规则设置：分成「加工件」「标准件」两栏（选项卡），互不挤压。
    ///   · 加工件：日期_材料_名称 的分段规则、图号来源与截断
    ///   · 标准件：前缀列表 + 一排前缀按钮（点一下即可增删），以及 BOM 收录开关
    /// 底部是实时预览：输入一个零件名，立刻看到解析结果与是否进 BOM。
    /// </summary>
    internal sealed class NamingRuleForm : Form
    {
        /// <summary>常用前缀，点一下就加进前缀列表。</summary>
        private static readonly string[] PresetPrefixes =
        {
            "电机", "电气", "淘宝", "气动", "液压", "标准件", "外购件", "轴承", "紧固件", "传感器"
        };

        private readonly IAddinHost _host;

        // 加工件栏
        private readonly TextBox _separator;
        private readonly ComboBox _materialSegment;
        private readonly ComboBox _partNumberSource;
        private readonly ComboBox _cutRule;
        private readonly TextBox _pattern;

        // 标准件栏
        private readonly TextBox _prefixes;
        private readonly FlowLayoutPanel _usedPrefixPanel;
        private readonly FlowLayoutPanel _presetPanel;
        private readonly CheckBox _requirePattern;

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
            _materialSegment = new ComboBox();
            _partNumberSource = new ComboBox();
            _cutRule = new ComboBox();
            _pattern = Theme.CreateTextBox();
            _prefixes = Theme.CreateTextBox();
            _usedPrefixPanel = new FlowLayoutPanel();
            _presetPanel = new FlowLayoutPanel();
            _requirePattern = new CheckBox();
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
            WindowLayout.Attach(this, _host.Settings, new Size(900, 700), new Size(820, 620));

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
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 64f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 36f));

            layout.Controls.Add(BuildTabs(), 0, 0);
            layout.Controls.Add(BuildPreviewPanel(), 0, 1);
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
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                BackColor = Theme.Surface
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            for (var i = 0; i < 4; i++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            }
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            layout.Controls.Add(Theme.CreateLabel("加工件命名规则（进 BOM）", Theme.BodyBold, Theme.Text), 0, 0);
            layout.Controls.Add(BuildField("分段符号", _separator,
                "各段之间的分隔符，默认下划线 _"), 0, 1);

            _materialSegment.DropDownStyle = ComboBoxStyle.DropDownList;
            _materialSegment.Font = Theme.Body;
            _materialSegment.Items.AddRange(new object[]
            {
                "第 2 段是材料（日期_材料_名称）",
                "第 3 段是材料（日期_类别_材料_名称）",
                "最后一段是材料",
                "不从名称取材料"
            });
            layout.Controls.Add(BuildField("材料段", _materialSegment,
                "例：20260908_6061_扫码枪安装板 → 材料 = 6061"), 0, 2);

            _partNumberSource.DropDownStyle = ComboBoxStyle.DropDownList;
            _partNumberSource.Font = Theme.Body;
            _partNumberSource.Items.AddRange(new object[]
            {
                "用零件文件名（推荐）",
                "优先读自定义属性「图号/零件号」",
                "只用自定义属性"
            });
            layout.Controls.Add(BuildField("图号来源", _partNumberSource,
                "图号取文件名，还是零件自定义属性里的图号"), 0, 3);

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
                "图号需要截断时选一项；一般保持「完整文件名」"), 0, 4);

            layout.Controls.Add(Theme.CreateLabel(
                "说明：只要名称第一段是日期（如 20260908），就判定为加工件并进入 BOM。",
                Theme.Small, Theme.Muted), 0, 5);

            return layout;
        }

        private Control BuildStandardPanel()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                BackColor = Theme.Surface
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            layout.Controls.Add(Theme.CreateLabel("标准件命名规则（前缀开头，也进 BOM）", Theme.BodyBold, Theme.Text), 0, 0);
            layout.Controls.Add(BuildField("前缀列表", _prefixes, "逗号分隔；下面点按钮也能增删"), 0, 1);

            layout.Controls.Add(Theme.CreateLabel("当前前缀（点 × 删除）", Theme.Body, Theme.Text), 0, 2);
            _usedPrefixPanel.Dock = DockStyle.Fill;
            _usedPrefixPanel.AutoScroll = true;
            _usedPrefixPanel.WrapContents = true;
            _usedPrefixPanel.BackColor = Theme.Surface;
            layout.Controls.Add(_usedPrefixPanel, 0, 3);

            layout.Controls.Add(Theme.CreateLabel("常用前缀（点一下加进列表）", Theme.Body, Theme.Text), 0, 4);
            _presetPanel.Dock = DockStyle.Fill;
            _presetPanel.AutoScroll = true;
            _presetPanel.WrapContents = true;
            _presetPanel.BackColor = Theme.Surface;
            foreach (var preset in PresetPrefixes)
            {
                var name = preset;
                var button = Theme.CreateSecondaryButton(name);
                button.Width = 74;
                button.Height = 26;
                button.Margin = new Padding(0, 0, 6, 6);
                button.Click += delegate { AddPrefix(name); };
                _presetPanel.Controls.Add(button);
            }
            layout.Controls.Add(_presetPanel, 0, 5);

            _requirePattern.Text = "只收录加工件（日期开头）与标准件（前缀开头）；其余子零件不进 BOM";
            _requirePattern.AutoSize = true;
            _requirePattern.ForeColor = Theme.Text;
            _requirePattern.Margin = new Padding(0, 10, 0, 0);
            layout.Controls.Add(_requirePattern, 0, 6);

            return layout;
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
            _materialSegment.SelectedIndexChanged += delegate { UpdatePreview(); };
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
                    _usedPrefixPanel.Controls.Add(Theme.CreateLabel("（还没有前缀，从下面点一个添加）", Theme.Small, Theme.Muted));
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

            _prefixes.Text = string.Join(",", prefixes.ToArray());
        }

        private void RemovePrefix(string prefix)
        {
            var prefixes = new List<string>(NamingOptionsFactory.ParsePrefixes(_prefixes.Text));
            prefixes.Remove(prefix);
            _prefixes.Text = string.Join(",", prefixes.ToArray());
        }

        private void LoadFromSettings()
        {
            var s = _host.Settings;
            _separator.Text = string.IsNullOrEmpty(s.SegmentSeparator) ? "_" : s.SegmentSeparator;
            _materialSegment.SelectedIndex = MaterialSegmentIndex(s.MaterialSegment);
            _prefixes.Text = s.BomPrefixes;
            _requirePattern.Checked = s.BomRequirePattern;
            _partNumberSource.SelectedIndex = Math.Max(0, Math.Min(2, s.PartNumberSource));
            _cutRule.SelectedIndex = Math.Max(0, Math.Min(4, s.PartNumberCutRule));
            _pattern.Text = s.PartNumberPattern;
            _sample.Text = "20260908_6061_扫码枪安装板";
        }

        private static int MaterialSegmentIndex(int segment)
        {
            switch (segment)
            {
                case -1:
                    return 2;
                case -3:
                    return 1;
                case 0:
                    return 3;
                default:
                    return 0;
            }
        }

        private static int MaterialSegmentValue(int index)
        {
            switch (index)
            {
                case 1:
                    return -3;
                case 2:
                    return -1;
                case 3:
                    return 0;
                default:
                    return -2;
            }
        }

        private void ResetToDefault()
        {
            _separator.Text = "_";
            _materialSegment.SelectedIndex = 0;
            _prefixes.Text = "电机,电气,淘宝";
            _requirePattern.Checked = true;
            _partNumberSource.SelectedIndex = 0;
            _cutRule.SelectedIndex = 0;
            _pattern.Text = string.Empty;
            UpdatePrefixButtons();
            UpdatePreview();
        }

        private void Save()
        {
            var s = _host.Settings;
            s.SegmentSeparator = string.IsNullOrEmpty(_separator.Text) ? "_" : _separator.Text;
            s.MaterialSegment = MaterialSegmentValue(_materialSegment.SelectedIndex);
            s.BomPrefixes = _prefixes.Text.Trim();
            s.BomRequirePattern = _requirePattern.Checked;
            s.PartNumberSource = _partNumberSource.SelectedIndex;
            s.PartNumberCutRule = _cutRule.SelectedIndex;
            s.PartNumberPattern = _pattern.Text.Trim();
            s.UseNameSegments = true;
            s.Save();

            Log.Info("命名规则已保存：加工件=日期_材料_名称；标准件前缀=" + s.BomPrefixes);
            Close();
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
                    MaterialSegment = MaterialSegmentValue(_materialSegment.SelectedIndex),
                    NameSegment = -1,
                    BomPrefixes = NamingOptionsFactory.ParsePrefixes(_prefixes.Text),
                    RequireBomPattern = _requirePattern.Checked
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
                    ? "✔ 进 BOM（" + (NamingOptions.StartsWithDate(fileName) ? "加工件" : "标准件") + "）"
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
