using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using MechKit.Core;

namespace MechKit.UI
{
    /// <summary>关于窗口：版本信息与日志/安装位置。</summary>
    internal sealed class AboutForm : Form
    {
        private readonly IAddinHost _host;

        public AboutForm(IAddinHost host)
        {
            _host = host;
            BuildLayout();
        }

        private void BuildLayout()
        {
            Text = "关于 " + AddinConstants.Title;
            Font = Theme.Body;
            BackColor = Theme.Canvas;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            WindowLayout.Attach(this, _host.Settings, new Size(520, 380), new Size(520, 380));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Theme.Accent };
            var title = Theme.CreateLabel(AddinConstants.Title, Theme.Title, Color.White);
            title.Location = new Point(18, 14);
            var version = Theme.CreateLabel("版本 " + Version, Theme.Small, Color.FromArgb(214, 232, 248));
            version.Location = new Point(19, 40);
            header.Controls.Add(title);
            header.Controls.Add(version);

            var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas, Padding = new Padding(18, 14, 18, 10) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6,
                BackColor = Theme.Canvas
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (var i = 0; i < 5; i++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            }

            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            AddRow(layout, 0, "插件标识", AddinConstants.AddinGuid);
            AddRow(layout, 1, "程序集", Assembly.GetExecutingAssembly().Location);
            AddRow(layout, 2, "配置目录", AppPaths.Root);
            AddRow(layout, 3, "日志目录", AppPaths.Logs);
            AddRow(layout, 4, "SOLIDWORKS", DescribeSolidWorks());

            var tips = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Canvas,
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Text = "使用提示：" + Environment.NewLine +
                       "· 命令位于 CommandManager 的「MechKit」选项卡，同时出现在「工具」菜单下。" + Environment.NewLine +
                       "· 批量导出支持工程图（PDF / DWG / DXF）与模型（STEP / IGES / STL）。" + Environment.NewLine +
                       "· 属性写入默认只处理文档级自定义属性，不修改配置特定属性。"
            };
            layout.Controls.Add(tips, 0, 5);
            layout.SetColumnSpan(tips, 2);

            body.Controls.Add(layout);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Theme.Canvas, Padding = new Padding(18, 8, 18, 12) };
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Canvas,
                AutoSize = true
            };

            var openLogs = Theme.CreateSecondaryButton("打开日志目录");
            openLogs.Width = 130;
            openLogs.Click += delegate
            {
                AppPaths.Ensure(AppPaths.Logs);
                Process.Start("explorer.exe", AppPaths.Logs);
            };

            var close = Theme.CreatePrimaryButton("关闭");
            close.Width = 90;
            close.Click += delegate { Close(); };

            buttons.Controls.Add(openLogs);
            buttons.Controls.Add(close);
            footer.Controls.Add(buttons);

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);
        }

        private static void AddRow(TableLayoutPanel table, int row, string caption, string value)
        {
            var label = Theme.CreateFieldLabel(caption);
            label.Dock = DockStyle.Fill;

            var text = Theme.CreateValueLabel(value);
            text.Dock = DockStyle.Fill;

            table.Controls.Add(label, 0, row);
            table.Controls.Add(text, 1, row);
        }

        private string DescribeSolidWorks()
        {
            try
            {
                var app = _host.SwApp;
                return app == null ? "未连接" : "SolidWorks " + app.RevisionNumber();
            }
            catch
            {
                return "未知";
            }
        }

        private static string Version
        {
            get
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                return version == null ? "0.1.0" : version.ToString();
            }
        }
    }
}
