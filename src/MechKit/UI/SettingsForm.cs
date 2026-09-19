using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MechKit.Core;

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
        private readonly ComboBox _assemblyLevel;
        private readonly ComboBox _standardNameField;
        private readonly ComboBox _standardMaterialField;
        private readonly ComboBox _standardProcessField;
        private readonly ComboBox _standardRemarkField;
        private readonly ComboBox _machinedNameField;
        private readonly ComboBox _machinedMaterialField;
        private readonly ComboBox _machinedProcessField;
        private readonly ComboBox _machinedRemarkField;
        private readonly Label _status;

        public SettingsForm(IAddinHost host)
        {
            _host = host;
            _weldment = Theme.CreateTextBox();
            _template = Theme.CreateTextBox();
            _macro = Theme.CreateTextBox();
            _toolbox = Theme.CreateTextBox();
            _prefixes = Theme.CreateTextBox();
            _requirePattern = new CheckBox();
            _assemblyLevel = new ComboBox();
            _standardNameField = CreateFieldSourceCombo();
            _standardMaterialField = CreateFieldSourceCombo();
            _standardProcessField = CreateFieldSourceCombo();
            _standardRemarkField = CreateFieldSourceCombo();
            _machinedNameField = CreateFieldSourceCombo();
            _machinedMaterialField = CreateFieldSourceCombo();
            _machinedProcessField = CreateFieldSourceCombo();
            _machinedRemarkField = CreateFieldSourceCombo();
            _status = Theme.CreateValueLabel("就绪");

            BuildLayout();
            LoadFromSettings();
        }

        private void BuildLayout()
        {
            Text = "MechKit 设置";
            Font = Theme.Body;
            BackColor = Theme.Canvas;
            StartPosition = FormStartPosition.CenterParent;
            WindowLayout.Attach(this, _host.Settings, new Size(980, 680), new Size(880, 580));

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
                RowCount = 5,
                BackColor = Theme.Surface
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));

            var heading = Theme.CreateLabel("BOM 表格格式", Theme.BodyBold, Theme.Text);
            heading.Dock = DockStyle.Fill;
            layout.Controls.Add(heading, 0, 0);
            layout.SetColumnSpan(heading, 2);

            _assemblyLevel.DropDownStyle = ComboBoxStyle.DropDownList;
            _assemblyLevel.Font = Theme.Body;
            _assemblyLevel.Dock = DockStyle.Left;
            _assemblyLevel.Width = 270;
            _assemblyLevel.Items.AddRange(new object[]
            {
                "仅顶层装配体",
                "展开到第 1 级子装配体",
                "展开到第 2 级子装配体",
                "展开到第 3 级子装配体",
                "展开到第 4 级子装配体",
                "展开到第 5 级子装配体",
                "完整路径（到最小单位）"
            });
            layout.Controls.Add(BomLabel("位置显示层级"), 0, 1);
            layout.Controls.Add(_assemblyLevel, 1, 1);

            _requirePattern.Text = "只收录加工件（日期段开头）与标准件（已配置前缀开头）";
            _requirePattern.AutoSize = true;
            _requirePattern.ForeColor = Theme.Text;
            _requirePattern.Margin = new Padding(0, 8, 0, 0);
            layout.Controls.Add(BomLabel("收录范围"), 0, 2);
            layout.Controls.Add(_requirePattern, 1, 2);

            var mapping = BuildFieldMappingTable();
            layout.Controls.Add(BomLabel("表头字段映射"), 0, 3);
            layout.Controls.Add(mapping, 1, 3);

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
                using (var form = new NamingRuleForm(_host))
                {
                    form.ShowDialog(this);
                }

                LoadFromSettings();
            };
            actions.Controls.Add(save);
            actions.Controls.Add(naming);
            layout.Controls.Add(actions, 1, 4);

            panel.Controls.Add(layout);
            return panel;
        }

        private Control BuildFieldMappingTable()
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 158,
                ColumnCount = 9,
                RowCount = 3,
                BackColor = Color.FromArgb(248, 249, 250),
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
                Padding = new Padding(0)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72f));
            for (var i = 0; i < 8; i++)
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12.5f));
            }
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));

            var headers = new[] { "", "序号", "位置", "属性", "零件名", "材料", "工艺", "数量", "备注" };
            for (var column = 0; column < headers.Length; column++)
            {
                var label = Theme.CreateLabel(headers[column], Theme.Small, Theme.Text);
                label.Dock = DockStyle.Fill;
                label.TextAlign = ContentAlignment.MiddleCenter;
                table.Controls.Add(label, column, 0);
            }

            AddMappingRow(table, 1, "标准件",
                BuildFixedCombo("自动序号"), BuildFixedCombo("装配位置"), BuildFixedCombo("标准件"),
                _standardNameField, _standardMaterialField, _standardProcessField,
                BuildFixedCombo("统计数量"), _standardRemarkField);
            AddMappingRow(table, 2, "加工件",
                BuildFixedCombo("自动序号"), BuildFixedCombo("装配位置"), BuildFixedCombo("加工件"),
                _machinedNameField, _machinedMaterialField, _machinedProcessField,
                BuildFixedCombo("统计数量"), _machinedRemarkField);

            return table;
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

        private static ComboBox CreateFieldSourceCombo()
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = Theme.Small
            };
            combo.Items.AddRange(new object[]
            {
                "自动规则",
                "完整文件名",
                "第1段",
                "第2段",
                "第3段",
                "第4段",
                "第5段",
                "第6段",
                "第7段",
                "第8段",
                "属性:名称",
                "属性:材料",
                "属性:工艺",
                "属性:备注",
                "留空",
                "第3段及以后"
            });
            return combo;
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
                using (var form = new NamingRuleForm(_host))
                {
                    form.ShowDialog(this);
                }

                LoadFromSettings();
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

            _requirePattern.Text = "只收录加工件（日期开头）与标准件（前缀开头）";
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
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(12, 10, 12, 10), Margin = new Padding(0, 8, 0, 0) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                BackColor = Theme.Surface
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            layout.Controls.Add(Theme.CreateLabel("个人设置迁移", Theme.BodyBold, Theme.Text), 0, 0);

            var exportHint = Theme.CreateLabel(
                "导出：把当前 SOLIDWORKS 的个人设置保存成一个文件（界面布局、笔势、快捷键、文件位置、导出选项等）",
                Theme.Small, Theme.Muted);
            exportHint.Dock = DockStyle.Fill;
            layout.Controls.Add(exportHint, 0, 1);

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
            layout.Controls.Add(exportActions, 0, 2);

            var importHint = Theme.CreateLabel(
                "导入：在另一台电脑 / 另一个 SOLIDWORKS 上导入该文件，导入后重启 SOLIDWORKS 生效",
                Theme.Small, Theme.Muted);
            importHint.Dock = DockStyle.Fill;
            layout.Controls.Add(importHint, 0, 3);

            var importButton = Theme.CreateSecondaryButton("导入设置…");
            importButton.Width = 150;
            importButton.Dock = DockStyle.Left;
            importButton.Click += delegate { Import(); };
            layout.Controls.Add(importButton, 0, 4);

            panel.Controls.Add(layout);
            return panel;
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
            _assemblyLevel.SelectedIndex = AssemblyLevelToIndex(settings.BomAssemblyLevel);
            SelectFieldSource(_standardNameField, settings.BomStandardNameField);
            SelectFieldSource(_standardMaterialField, settings.BomStandardMaterialField);
            SelectFieldSource(_standardProcessField, settings.BomStandardProcessField);
            SelectFieldSource(_standardRemarkField, settings.BomStandardRemarkField);
            SelectFieldSource(_machinedNameField, settings.BomMachinedNameField);
            SelectFieldSource(_machinedMaterialField, settings.BomMachinedMaterialField);
            SelectFieldSource(_machinedProcessField, settings.BomMachinedProcessField);
            SelectFieldSource(_machinedRemarkField, settings.BomMachinedRemarkField);
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
            settings.BomAssemblyLevel = AssemblyLevelFromIndex(_assemblyLevel.SelectedIndex);
            settings.BomStandardNameField = FieldSourceCode(_standardNameField);
            settings.BomStandardMaterialField = FieldSourceCode(_standardMaterialField);
            settings.BomStandardProcessField = FieldSourceCode(_standardProcessField);
            settings.BomStandardRemarkField = FieldSourceCode(_standardRemarkField);
            settings.BomMachinedNameField = FieldSourceCode(_machinedNameField);
            settings.BomMachinedMaterialField = FieldSourceCode(_machinedMaterialField);
            settings.BomMachinedProcessField = FieldSourceCode(_machinedProcessField);
            settings.BomMachinedRemarkField = FieldSourceCode(_machinedRemarkField);
            settings.Save();
            _status.Text = "设置已保存。";
        }

        private static string FieldSourceCode(ComboBox combo)
        {
            var index = combo == null ? 0 : combo.SelectedIndex;
            if (index <= 0) return "auto";
            if (index == 1) return "whole";
            if (index >= 2 && index <= 9) return "segment:" + (index - 1);
            switch (index)
            {
                case 10: return "property:name";
                case 11: return "property:material";
                case 12: return "property:process";
                case 13: return "property:remark";
                case 14: return "empty";
                case 15: return "tail:3";
                default: return "auto";
            }
        }

        private static void SelectFieldSource(ComboBox combo, string code)
        {
            if (combo == null)
            {
                return;
            }

            var value = (code ?? "auto").Trim().ToLowerInvariant();
            var index = 0;
            if (value == "whole") index = 1;
            else if (value == "property:name") index = 10;
            else if (value == "property:material") index = 11;
            else if (value == "property:process") index = 12;
            else if (value == "property:remark") index = 13;
            else if (value == "empty") index = 14;
            else if (value == "tail:3") index = 15;
            else if (value.StartsWith("segment:", StringComparison.Ordinal))
            {
                int segment;
                if (int.TryParse(value.Substring("segment:".Length), out segment) && segment >= 1 && segment <= 8)
                {
                    index = segment + 1;
                }
            }

            combo.SelectedIndex = index;
        }

        private static int AssemblyLevelToIndex(int value)
        {
            return value < 0 ? 6 : Math.Min(value, 5);
        }

        private static int AssemblyLevelFromIndex(int index)
        {
            return index >= 6 || index < 0 ? -1 : index;
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
                dialog.FileName = string.Format("MechKit设置_{0:yyyyMMdd_HHmmss}{1}",
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
