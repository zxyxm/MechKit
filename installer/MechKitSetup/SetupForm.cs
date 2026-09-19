using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MechKit.Setup
{
    internal sealed class SetupForm : Form
    {
        private static readonly Color Accent = Color.FromArgb(15, 108, 189);
        private static readonly Color AccentDark = Color.FromArgb(12, 82, 143);
        private static readonly Color Canvas = Color.FromArgb(246, 247, 249);
        private static readonly Color TextColor = Color.FromArgb(32, 36, 40);
        private static readonly Color Muted = Color.FromArgb(110, 118, 126);

        private readonly AddinRegistrar _registrar;
        private readonly Label _status;
        private readonly TextBox _log;
        private readonly Button _install;
        private readonly Button _uninstall;
        private readonly Button _openFolder;
        private readonly CheckBox _startWithSolidWorks;
        private readonly CheckBox _removeFiles;

        private AddinInfo _info;

        public SetupForm(AddinRegistrar registrar)
        {
            _registrar = registrar;

            _status = new Label();
            _log = new TextBox();
            _install = CreateButton("安装", true);
            _uninstall = CreateButton("卸载", false);
            _openFolder = CreateButton("打开安装目录", false);
            _startWithSolidWorks = new CheckBox();
            _removeFiles = new CheckBox();

            BuildLayout();
            Load += delegate { RefreshState(); };
        }

        private void BuildLayout()
        {
            Text = "MechKit 安装程序";
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = Canvas;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(620, 470);

            var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Accent };
            var title = new Label
            {
                Text = "MechKit",
                Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(18, 12)
            };
            var subtitle = new Label
            {
                Text = "图号 / 材料 / 加工件数量 —— SOLIDWORKS 插件",
                Font = new Font("Microsoft YaHei UI", 8.5f),
                ForeColor = Color.FromArgb(214, 232, 248),
                AutoSize = true,
                Location = new Point(20, 44)
            };
            header.Controls.Add(title);
            header.Controls.Add(subtitle);

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 12, 18, 6), BackColor = Canvas };

            var info = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 96,
                ColumnCount = 2,
                RowCount = 4,
                BackColor = Canvas
            };
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (var i = 0; i < 4; i++)
            {
                info.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
            }

            AddRow(info, 0, "插件名称", "MechKit");
            AddRow(info, 1, "安装目录", _registrar.InstallDirectory);
            AddRow(info, 2, "程序集", Path.Combine(_registrar.PayloadDirectory, "MechKit.dll"));

            _status.Dock = DockStyle.Fill;
            _status.ForeColor = Muted;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            info.Controls.Add(NewCaption("当前状态"), 0, 3);
            info.Controls.Add(_status, 1, 3);

            _startWithSolidWorks.Text = "随 SOLIDWORKS 启动时自动加载";
            _startWithSolidWorks.Checked = true;
            _startWithSolidWorks.AutoSize = true;
            _startWithSolidWorks.ForeColor = TextColor;

            _removeFiles.Text = "卸载时同时删除安装目录";
            _removeFiles.AutoSize = true;
            _removeFiles.ForeColor = TextColor;

            var options = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 30,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Canvas
            };
            options.Controls.Add(_startWithSolidWorks);
            _startWithSolidWorks.Margin = new Padding(0, 6, 24, 0);
            options.Controls.Add(_removeFiles);
            _removeFiles.Margin = new Padding(0, 6, 0, 0);

            var logCaption = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                Text = "安装日志",
                Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
                ForeColor = TextColor
            };

            _log.Dock = DockStyle.Fill;
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.Font = new Font("Consolas", 8.5f);
            _log.BackColor = Color.FromArgb(250, 251, 252);

            body.Controls.Add(_log);
            body.Controls.Add(logCaption);
            body.Controls.Add(options);
            body.Controls.Add(info);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = Canvas, Padding = new Padding(18, 8, 18, 14) };
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                BackColor = Canvas
            };

            _install.Width = 96;
            _uninstall.Width = 96;
            _openFolder.Width = 120;

            var close = CreateButton("关闭", false);
            close.Width = 88;

            _install.Click += delegate { DoInstall(); };
            _uninstall.Click += delegate { DoUninstall(); };
            _openFolder.Click += delegate { OpenInstallFolder(); };
            close.Click += delegate { Close(); };

            buttons.Controls.Add(_install);
            buttons.Controls.Add(_uninstall);
            buttons.Controls.Add(_openFolder);
            buttons.Controls.Add(close);
            footer.Controls.Add(buttons);

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);
        }

        private static void AddRow(TableLayoutPanel table, int row, string caption, string value)
        {
            var label = NewCaption(caption);
            var text = new Label
            {
                Text = value,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                ForeColor = TextColor
            };

            table.Controls.Add(label, 0, row);
            table.Controls.Add(text, 1, row);
        }

        private static Label NewCaption(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Muted
            };
        }

        private static Button CreateButton(string text, bool primary)
        {
            var button = new Button
            {
                Text = text,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 9f),
                BackColor = primary ? Accent : Color.White,
                ForeColor = primary ? Color.White : TextColor,
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand,
                Margin = new Padding(6, 0, 0, 0)
            };
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(222, 226, 230);
            button.FlatAppearance.MouseOverBackColor = primary ? AccentDark : Color.FromArgb(238, 242, 246);
            return button;
        }

        private void RefreshState()
        {
            try
            {
                var payloadDll = Path.Combine(_registrar.PayloadDirectory, "MechKit.dll");
                _info = _registrar.ReadInfo(payloadDll);

                string installedVersion;
                var installed = _registrar.IsInstalled(out installedVersion);

                _status.Text = installed
                    ? string.Format("已安装（{0}）", installedVersion ?? "未知版本")
                    : "未安装";
                _status.ForeColor = installed ? Color.FromArgb(16, 124, 65) : Muted;

                _install.Text = installed ? "重新安装" : "安装";
                _uninstall.Enabled = installed;

                AppendLog(string.Format("准备安装：{0} v{1}", _info.Title, _info.Version));
                AppendLog("安装目录：" + _registrar.InstallDirectory);
            }
            catch (Exception ex)
            {
                _status.Text = "安装包不完整";
                _status.ForeColor = Color.FromArgb(180, 45, 55);
                _install.Enabled = false;
                _uninstall.Enabled = false;
                AppendLog("错误：" + ex.Message);
            }
        }

        private void DoInstall()
        {
            try
            {
                AppendLog("—— 开始安装 ——");
                _registrar.Install(_info, _startWithSolidWorks.Checked, AppendLog);
                AppendLog("—— 安装完成，重启 SOLIDWORKS 即可看到「MechKit」选项卡与工具栏 ——");
                RefreshState();

                MessageBox.Show(this,
                    "安装完成。\r\n\r\n请关闭并重新启动 SOLIDWORKS：\r\n" +
                    "· CommandManager 会多出「MechKit」选项卡\r\n" +
                    "· 工具栏会出现同名工具条\r\n" +
                    "· 「工具 → 插件」列表中已勾选「启动」",
                    "MechKit", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AppendLog("安装失败：" + ex.Message);
                MessageBox.Show(this, "安装失败：" + ex.Message, "MechKit",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DoUninstall()
        {
            if (MessageBox.Show(this, "确定要卸载 MechKit吗？", "MechKit",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                AppendLog("—— 开始卸载 ——");
                _registrar.Uninstall(_removeFiles.Checked, AppendLog);
                AppendLog("—— 卸载完成，重启 SOLIDWORKS 生效 ——");
                RefreshState();
            }
            catch (Exception ex)
            {
                AppendLog("卸载失败：" + ex.Message);
                MessageBox.Show(this, "卸载失败：" + ex.Message, "MechKit",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenInstallFolder()
        {
            try
            {
                if (Directory.Exists(_registrar.InstallDirectory))
                {
                    Process.Start("explorer.exe", _registrar.InstallDirectory);
                }
                else
                {
                    Process.Start("explorer.exe", _registrar.PayloadDirectory);
                }
            }
            catch
            {
                // 打开目录失败不影响主流程
            }
        }

        private void AppendLog(string message)
        {
            _log.AppendText(message + Environment.NewLine);
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }
    }
}
