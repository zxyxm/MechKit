using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MechKit.Core;
using MechKit.UI;

namespace MechKit.Harness
{
    /// <summary>
    /// 离线 UI 测试宿主：不启动 SOLIDWORKS、不注册插件，
    /// 直接打开 MechKit 的各个窗口，用来快速检查界面与交互。
    /// </summary>
    internal sealed class HarnessForm : Form
    {
        private static readonly Color Accent = Color.FromArgb(15, 108, 189);
        private readonly FakeHost _host = new FakeHost();
        private readonly TextBox _log = new TextBox();

        public HarnessForm()
        {
            Text = "MechKit 离线测试宿主（不需要 SOLIDWORKS）";
            Font = new Font("Microsoft YaHei UI", 9f);
            ClientSize = new Size(560, 520);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(246, 247, 249);

            var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Accent };
            header.Controls.Add(new Label
            {
                Text = "MechKit 离线测试宿主",
                Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(16, 10)
            });
            header.Controls.Add(new Label
            {
                Text = "不启动 SOLIDWORKS、不注册插件，直接预览各个窗口",
                Font = new Font("Microsoft YaHei UI", 8.5f),
                ForeColor = Color.FromArgb(214, 232, 248),
                AutoSize = true,
                Location = new Point(18, 38)
            });

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 132,
                Padding = new Padding(14, 10, 14, 6),
                BackColor = Color.FromArgb(246, 247, 249)
            };

            buttons.Controls.Add(CreateButton("★ 选项卡预览", delegate { Show(new TabPreviewForm(Execute)); }));
            buttons.Controls.Add(CreateButton("生成BOM", delegate { Execute("生成BOM"); }));
            buttons.Controls.Add(CreateButton("明细汇总", delegate { Execute("明细汇总"); }));
            buttons.Controls.Add(CreateButton("加工件命名规则", delegate { Execute("加工件命名规则"); }));
            buttons.Controls.Add(CreateButton("标准件前缀", delegate { Execute("标准件前缀"); }));
            buttons.Controls.Add(CreateButton("批量导出", delegate { Execute("批量导出"); }));
            buttons.Controls.Add(CreateButton("个人配置", delegate { Execute("个人配置"); }));
            buttons.Controls.Add(CreateButton("关于", delegate { Execute("关于"); }));
            buttons.Controls.Add(CreateButton("任务面板", delegate { ShowTaskPane(); }));

            _log.Dock = DockStyle.Fill;
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.Font = new Font("Consolas", 8.5f);
            _log.BackColor = Color.White;

            Controls.Add(_log);
            Controls.Add(buttons);
            Controls.Add(header);

            WindowLayout.EnableEscapeToClose(this);

            Log.Message += OnLogMessage;
            Log.Info("[harness] 离线宿主已启动，插件日志目录：" + AppPaths.Root);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Log.Message -= OnLogMessage;
            }

            base.Dispose(disposing);
        }

        private static Button CreateButton(string text, EventHandler onClick)
        {
            var button = new Button
            {
                Text = text,
                Width = 158,
                Height = 34,
                Margin = new Padding(0, 0, 8, 8),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(32, 36, 40),
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(222, 226, 230);
            button.Click += onClick;
            return button;
        }

        private void Show(Form form)
        {
            using (form)
            {
                form.ShowDialog(this);
            }
        }

        /// <summary>
        /// 与 SOLIDWORKS 里 OnXxx 回调走同一套逻辑，属于纯离线模拟：
        /// 生成BOM / 明细汇总 需要文档，因此这里只记录调用。
        /// </summary>
        private void Execute(string command)
        {
            Log.Info("[harness] 命令被调用：" + command);

            // 前缀按钮：点一下就加前缀（和 SOLIDWORKS 里 OnPrefixCommand 的行为一致）
            if (command.StartsWith("前缀:", StringComparison.Ordinal))
            {
                _host.ApplyPrefix(command.Substring("前缀:".Length), false);
                return;
            }

            switch (command)
            {
                case "生成BOM":
                    _host.GenerateBom();
                    break;
                case "明细汇总":
                    Show(new PartListForm(_host));
                    break;
                case "加工件命名规则":
                    Show(new NamingRuleForm(_host, 0));
                    break;
                case "标准件前缀":
                    Show(new NamingRuleForm(_host, 1));
                    break;
                case "批量导出":
                    Show(new BatchExportForm(_host));
                    break;
                case "属性工具":
                    Show(new PropertyToolForm(_host));
                    break;
                case "工具箱面板":
                    ShowTaskPane();
                    break;
                case "个人配置":
                    Show(new SettingsForm(_host));
                    break;
                case "关于":
                    Show(new AboutForm(_host));
                    break;
            }
        }

        private void ShowTaskPane()
        {
            var host = new Form
            {
                Text = "任务面板预览（离线）",
                ClientSize = new Size(380, 620),
                StartPosition = FormStartPosition.CenterParent,
                Font = new Font("Microsoft YaHei UI", 9f)
            };

            var control = new TaskPaneControl(_host) { Dock = DockStyle.Fill };
            host.Controls.Add(control);
            Show(host);
        }

        /// <summary>直接打开选项卡预览（配合 --tab 命令行参数，便于脚本化截图/演示）。</summary>
        public void OpenTabPreview()
        {
            Show(new TabPreviewForm(Execute));
        }

        /// <summary>把 1:1 选项卡客户端直接渲染成 PNG，便于无 SOLIDWORKS 的视觉回归。</summary>
        public void SaveTabPreview(string path)
        {
            var target = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var preview = new TabPreviewForm(Execute))
            {
                preview.Show(this);
                Application.DoEvents();

                preview.SaveClientImage(target);

                preview.Close();
            }

            Log.Info("[harness] 选项卡预览图已保存：" + target);
        }

        /// <summary>直接打开指定窗口，便于命令行预览：--settings / --partlist / --export / --property</summary>
        public void OpenNamed(string name)
        {
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "--settings":
                    Show(new SettingsForm(_host));
                    break;
                case "--partlist":
                    Show(new PartListForm(_host));
                    break;
                case "--export":
                    Show(new BatchExportForm(_host));
                    break;
                case "--property":
                    Show(new PropertyToolForm(_host));
                    break;
                case "--taskpane":
                    ShowTaskPane();
                    break;
                case "--naming":
                    Show(new NamingRuleForm(_host));
                    break;
            }
        }

        private void OnLogMessage(string message)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(OnLogMessage), message);
                return;
            }

            _log.AppendText(message + Environment.NewLine);
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var form = new HarnessForm();
            if (args != null && args.Length > 0)
            {
                var arg = args[0];
                if (string.Equals(arg, "--tab", StringComparison.OrdinalIgnoreCase))
                {
                    form.Shown += delegate { form.OpenTabPreview(); };
                }
                else if (arg.StartsWith("--tab-image=", StringComparison.OrdinalIgnoreCase))
                {
                    var imagePath = arg.Substring("--tab-image=".Length).Trim().Trim('"');
                    form.Shown += delegate
                    {
                        form.SaveTabPreview(imagePath);
                        form.Close();
                    };
                }
                else if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    form.Shown += delegate { form.OpenNamed(arg); };
                }
            }

            Application.Run(form);
        }
    }
}
