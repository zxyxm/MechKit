using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using MechKit.Core;

namespace MechKit.Harness
{
    /// <summary>
    /// 还原 SOLIDWORKS CommandManager 选项卡的离线预览：
    /// 顶部选项卡条 + MechKit 选项卡内容（和插件在 SW 里创建的按钮一一对应），
    /// 图标用的是插件内嵌的真实资源。
    /// </summary>
    internal sealed class TabPreviewForm : Form
    {
        private static readonly Color Accent = Color.FromArgb(15, 108, 189);
        private static readonly Color RibbonBack = Color.FromArgb(240, 242, 245);
        private static readonly Color TabBand = Color.FromArgb(250, 251, 252);
        private static readonly Color Border = Color.FromArgb(214, 219, 226);

        private readonly List<Bitmap> _icons = new List<Bitmap>();
        private readonly List<Bitmap> _smallIcons = new List<Bitmap>();
        private readonly Action<string> _onCommand;

        public TabPreviewForm(Action<string> onCommand)
        {
            _onCommand = onCommand;

            Text = "MechKit 选项卡预览（还原 SOLIDWORKS CommandManager）";
            Font = new Font("Microsoft YaHei UI", 9f);
            ClientSize = new Size(1180, 420);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = RibbonBack;

            LoadIcons();

            Controls.Add(BuildRibbon());
            Controls.Add(BuildToolbarStrip());
            Controls.Add(BuildTabStrip());
            Controls.Add(BuildFrame());

            MechKit.UI.WindowLayout.EnableEscapeToClose(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
            foreach (var icon in _icons)
            {
                icon.Dispose();
            }

            foreach (var icon in _smallIcons)
            {
                icon.Dispose();
            }
            }

            base.Dispose(disposing);
        }

        /// <summary>从插件内嵌资源里切出每个命令的图标（和 SOLIDWORKS 用的是同一批文件）。</summary>
        private void LoadIcons()
        {
            try
            {
                IconResources.Extract();
            }
            catch (Exception ex)
            {
                Log.Warn("[harness] 提取图标失败：" + ex.Message);
                return;
            }

            var wanted = new[] { "strip40.png", "strip20.png" };
            foreach (var path in IconResources.SmallIcons)
            {
                var fileName = Path.GetFileName(path);
                if (Array.IndexOf(wanted, fileName) < 0)
                {
                    continue;
                }

                using (var strip = new Bitmap(path))
                {
                    var size = strip.Height;
                    var count = strip.Width / size;
                    var target = fileName == "strip40.png" ? _icons : _smallIcons;
                    for (var i = 0; i < count; i++)
                    {
                        var rect = new Rectangle(i * size, 0, size, size);
                        target.Add(strip.Clone(rect, PixelFormat.Format32bppArgb));
                    }
                }
            }
        }

        private Control BuildFrame()
        {
            // 窗体外框：模拟 SW 主窗口的边线
            return new Panel { Dock = DockStyle.Left, Width = 0, BackColor = Border };
        }

        /// <summary>顶部的选项卡条，MechKit 高亮选中。</summary>
        private Control BuildTabStrip()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = TabBand };

            var tabs = new[]
            {
                "装配体", "布局", "草图", "标注", "评估", "SOLIDWORKS 插件", "MBD", "MechKit"
            };

            var x = 8;
            foreach (var tab in tabs)
            {
                var isMechKit = tab == "MechKit";
                var label = new Label
                {
                    Text = tab,
                    AutoSize = false,
                    Width = TextRenderer.MeasureText(tab, Font).Width + 24,
                    Height = 32,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Location = new Point(x, 0),
                    Font = isMechKit ? new Font(Font, FontStyle.Bold) : Font,
                    ForeColor = isMechKit ? Accent : Color.FromArgb(70, 76, 84),
                    BackColor = isMechKit ? Color.White : TabBand
                };

                var captured = label;
                captured.Paint += delegate(object sender, PaintEventArgs e)
                {
                    if (isMechKit)
                    {
                        using (var pen = new Pen(Accent, 2))
                        {
                            e.Graphics.DrawLine(pen, 4, captured.Height - 2,
                                captured.Width - 4, captured.Height - 2);
                        }
                    }
                };

                panel.Controls.Add(label);
                x += label.Width;
            }

            var hint = new Label
            {
                Text = "← 这是 SOLIDWORKS 里 MechKit 选项卡的位置（选项卡栏末尾）",
                AutoSize = true,
                ForeColor = Color.FromArgb(120, 128, 136),
                Location = new Point(x + 16, 8)
            };
            panel.Controls.Add(hint);

            return panel;
        }

        /// <summary>MechKit 选项卡的内容：和插件创建的 7 个命令一一对应。</summary>
        /// <summary>经典工具条样式：小图标 + 折行文字，和 SOLIDWORKS 顶部那排一样。</summary>
        private Control BuildToolbarStrip()
        {
            var commands = new[]
            {
                "一键生成BOM", "明细汇总（图号/材料/数量）", "加工件命名规则", "标准件前缀",
                "批量导出", "属性工具", "工具箱面板", "个人配置/设置迁移", "关于 MechKit"
            };

            var host = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = RibbonBack, Padding = new Padding(10, 8, 10, 4) };

            var caption = new Label
            {
                Text = "① SOLIDWORKS 顶部工具条（经典工具条样式，装好后就是这个样子）",
                Dock = DockStyle.Top,
                Height = 18,
                ForeColor = Color.FromArgb(110, 118, 126),
                Font = new Font(Font.FontFamily, 8f)
            };

            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = RibbonBack
            };

            for (var i = 0; i < commands.Length; i++)
            {
                var index = i;
                var button = new Button
                {
                    Text = commands[i],
                    Width = 62,
                    Height = 54,
                    Margin = new Padding(0, 0, 2, 0),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = RibbonBack,
                    ForeColor = Color.FromArgb(32, 36, 40),
                    TextAlign = ContentAlignment.BottomCenter,
                    ImageAlign = ContentAlignment.TopCenter,
                    Image = index < _smallIcons.Count ? _smallIcons[index] : null,
                    Padding = new Padding(0, 2, 0, 2),
                    UseVisualStyleBackColor = false,
                    Font = new Font(Font.FontFamily, 7.5f)
                };

                button.FlatAppearance.BorderColor = RibbonBack;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(226, 238, 250);
                button.Click += delegate { _onCommand(commands[index]); };
                row.Controls.Add(button);
            }

            host.Controls.Add(row);
            host.Controls.Add(caption);
            return host;
        }

        private Control BuildRibbon()
        {
            var host = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(10, 8, 10, 8) };

            var caption = new Label
            {
                Text = "② MechKit 选项卡（CommandManager）——点击任意按钮会打开对应的真实窗口",
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = Color.FromArgb(110, 118, 126),
                Font = new Font(Font.FontFamily, 8f)
            };
            host.Controls.Add(caption);

            var group = new Panel
            {
                // 左侧 = MechKit 命令组（大按钮），右侧 = 说明文字，两块并排不重叠
                Dock = DockStyle.Left,
                Width = 9 * 106 + 24,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(8, 6, 8, 6)
            };

            var commands = new[]
            {
                "一键生成BOM", "明细汇总（图号/材料/数量）", "加工件命名规则", "标准件前缀",
                "批量导出", "属性工具", "工具箱面板", "个人配置/设置迁移", "关于 MechKit"
            };

            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.White
            };

            for (var i = 0; i < commands.Length; i++)
            {
                var index = i;
                var button = new Button
                {
                    Text = commands[i],
                    Width = 104,
                    Height = 88,
                    Margin = new Padding(2, 2, 2, 2),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.White,
                    ForeColor = Color.FromArgb(32, 36, 40),
                    TextAlign = ContentAlignment.BottomCenter,
                    ImageAlign = ContentAlignment.TopCenter,
                    Image = index < _icons.Count ? _icons[index] : null,
                    Padding = new Padding(0, 4, 0, 16),
                    UseVisualStyleBackColor = false
                };

                button.FlatAppearance.BorderColor = Color.White;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(226, 238, 250);
                button.Click += delegate { _onCommand(commands[index]); };
                row.Controls.Add(button);
            }

            group.Controls.Add(row);

            var groupCaption = new Label
            {
                Text = "MechKit",
                Dock = DockStyle.Bottom,
                Height = 18,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(110, 118, 126),
                Font = new Font(Font.FontFamily, 8f)
            };
            group.Controls.Add(groupCaption);
            groupCaption.BringToFront();

            host.Controls.Add(group);

            // 第二组：标准件前缀按钮（和插件在选项卡上创建的第二组命令一致）
            var prefixes = NamingOptionsFactory.ParsePrefixes(AddinSettings.Load().BomPrefixes);
            if (prefixes.Length > 0)
            {
                var prefixGroup = new Panel
                {
                    Dock = DockStyle.Left,
                    Width = Math.Min(prefixes.Length, 12) * 78 + 20,
                    BackColor = Color.White,
                    BorderStyle = BorderStyle.FixedSingle,
                    Padding = new Padding(8, 6, 8, 6)
                };

                var prefixRow = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = true,
                    BackColor = Color.White
                };

                var count = Math.Min(prefixes.Length, 12);
                for (var i = 0; i < count; i++)
                {
                    var prefix = prefixes[i];
                    var button = new Button
                    {
                        Text = prefix,
                        Width = 70,
                        Height = 34,
                        Margin = new Padding(2),
                        FlatStyle = FlatStyle.Flat,
                        BackColor = Color.White,
                        ForeColor = Color.FromArgb(32, 36, 40),
                        UseVisualStyleBackColor = false
                    };
                    button.FlatAppearance.BorderColor = Color.FromArgb(214, 219, 226);
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(226, 238, 250);
                    button.Click += delegate { _onCommand("前缀:" + prefix); };
                    prefixRow.Controls.Add(button);
                }

                prefixGroup.Controls.Add(prefixRow);

                var prefixCaption = new Label
                {
                    Text = "标准件前缀",
                    Dock = DockStyle.Bottom,
                    Height = 18,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Color.FromArgb(110, 118, 126),
                    Font = new Font(Font.FontFamily, 8f)
                };
                prefixGroup.Controls.Add(prefixCaption);
                prefixCaption.BringToFront();

                host.Controls.Add(prefixGroup);
            }

            var info = new Label
            {
                Dock = DockStyle.Fill,
                Text = "点击按钮会调用与 SOLIDWORKS 里完全相同的处理逻辑（离线下没有文档，因此会走\"未打开文档\"分支）。\r\n\r\n" +
                       "· 生成BOM：汇总当前装配体，导出 CSV 并用 Excel 打开\r\n" +
                       "· 明细汇总：图号 / 材料 / 加工件数量，可导出或写回属性\r\n" +
                       "· 批量导出：PDF / DWG / DXF / STEP / IGES / STL\r\n" +
                       "· 属性工具：批量写入自定义属性\r\n" +
                       "· 工具箱面板：任务面板（含一键加/去前缀）\r\n" +
                       "· 个人配置：常用目录 + 个人设置导出导入（含笔势）",
                ForeColor = Color.FromArgb(120, 128, 136),
                Padding = new Padding(16, 4, 0, 0)
            };
            host.Controls.Add(info);
            info.BringToFront();

            return host;
        }
    }
}
