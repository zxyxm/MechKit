using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using MechKit.Core;

namespace MechKit.Harness
{
    /// <summary>
    /// SOLIDWORKS CommandManager 的离线 1:1 预览。
    /// 尺寸、按钮换行、图标位置与底部选项卡均按实际界面截图还原；
    /// 按钮仍调用 HarnessForm 中对应的真实窗口逻辑。
    /// </summary>
    internal sealed class TabPreviewForm : Form
    {
        private static readonly Color RibbonBack = Color.FromArgb(245, 245, 245);
        private static readonly Color TabBack = Color.FromArgb(230, 230, 230);
        private static readonly Color SelectedTabBack = Color.FromArgb(250, 250, 250);
        private static readonly Color Border = Color.FromArgb(139, 139, 139);
        private static readonly Color TextColor = Color.FromArgb(18, 18, 18);

        private readonly List<Bitmap> _icons = new List<Bitmap>();
        private readonly Action<string> _onCommand;
        private readonly Panel _canvas;

        public TabPreviewForm(Action<string> onCommand)
        {
            _onCommand = onCommand;

            Text = "MechKit 选项卡离线测试";
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(1050, 150);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = RibbonBack;
            MaximizeBox = false;

            LoadIcons();

            _canvas = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = RibbonBack
            };
            _canvas.Controls.Add(BuildCommandArea());
            _canvas.Controls.Add(BuildTabStrip());
            Controls.Add(_canvas);

            MechKit.UI.WindowLayout.EnableEscapeToClose(this);
        }

        /// <summary>只保存 CommandManager 客户区，不包含 Windows 标题栏和边框。</summary>
        public void SaveClientImage(string path)
        {
            _canvas.PerformLayout();
            using (var bitmap = new Bitmap(_canvas.ClientSize.Width, _canvas.ClientSize.Height))
            {
                _canvas.DrawToBitmap(bitmap, new Rectangle(Point.Empty, _canvas.ClientSize));
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var icon in _icons)
                {
                    icon.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        /// <summary>从插件内嵌 strip40.png 中切出与 SOLIDWORKS 相同的命令图标。</summary>
        private void LoadIcons()
        {
            try
            {
                IconResources.Extract();
                foreach (var path in IconResources.SmallIcons)
                {
                    if (!string.Equals(Path.GetFileName(path), "strip40.png",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    using (var strip = new Bitmap(path))
                    {
                        var size = strip.Height;
                        var count = strip.Width / size;
                        for (var i = 0; i < count; i++)
                        {
                            var rect = new Rectangle(i * size, 0, size, size);
                            _icons.Add(strip.Clone(rect, PixelFormat.Format32bppArgb));
                        }
                    }

                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("[harness] 提取选项卡图标失败：" + ex.Message);
            }
        }

        /// <summary>截图上方的 MechKit 命令区；标准件设置后紧跟已配置的前缀按钮。</summary>
        private Control BuildCommandArea()
        {
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = RibbonBack
            };
            host.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (var pen = new Pen(Border))
                {
                    e.Graphics.DrawLine(pen, 0, host.Height - 1, host.Width, host.Height - 1);
                }
            };

            var commands = new List<CommandSpec>
            {
                new CommandSpec("一键生成BOM", "一键生\r\n成 BOM\r\n表", 76, 0),
                new CommandSpec("明细汇总（图号/材料/数量）", "明细汇总（\r\n图号/材料\r\n/数量）", 84, 1),
                new CommandSpec("加工件命名规则", "加工件\r\n命名规\r\n则设置", 69, 2),
                new CommandSpec("标准件前缀", "标准件\r\n前缀设\r\n置", 63, 3)
            };

            var prefixes = NamingOptionsFactory.ParsePrefixes(AddinSettings.Load().BomPrefixes);
            for (var prefixIndex = 0; prefixIndex < prefixes.Length &&
                    prefixIndex < AddinConstants.MaxPrefixCommands; prefixIndex++)
            {
                var prefix = prefixes[prefixIndex];
                commands.Add(new CommandSpec("前缀:" + prefix, prefix,
                    Math.Max(50, TextRenderer.MeasureText(prefix, Font).Width + 18), 10 + prefixIndex));
            }

            commands.AddRange(new[]
            {
                new CommandSpec("批量导出", "批量\r\n导出", 49, 4),
                new CommandSpec("工具箱面板", "工具\r\n箱面\r\n板", 45, 6)
            });

            var x = 0;
            for (var i = 0; i < commands.Count; i++)
            {
                var command = commands[i];
                var capturedId = command.Id;
                var button = new CommandButton(
                    command.DisplayText,
                    command.IconIndex < _icons.Count ? _icons[command.IconIndex] : null)
                {
                    Location = new Point(x, 0),
                    Size = new Size(command.Width, 117),
                    // 参考截图中鼠标停在“标准件前缀”按钮上的状态。
                    Highlighted = string.Equals(command.Id, "标准件前缀", StringComparison.Ordinal)
                };
                button.Click += delegate { _onCommand(capturedId); };
                host.Controls.Add(button);
                x += command.Width;
            }

            return host;
        }

        /// <summary>截图底部的 SOLIDWORKS 选项卡条，MechKit 保持选中。</summary>
        private Control BuildTabStrip()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 33,
                BackColor = RibbonBack
            };

            var tabs = new[]
            {
                new TabSpec("装配体", 72),
                new TabSpec("布局", 59),
                new TabSpec("草图", 57),
                new TabSpec("标注", 56),
                new TabSpec("评估", 56),
                new TabSpec("SOLIDWORKS 插件", 176),
                new TabSpec("MBD", 60),
                new TabSpec("MechKit", 82),
                new TabSpec("嘉立创Ican机械设计", 191),
                new TabSpec("嘉立创Ican工具箱", 171)
            };

            var x = 0;
            foreach (var tab in tabs)
            {
                var selected = string.Equals(tab.Text, "MechKit", StringComparison.Ordinal);
                var label = new Label
                {
                    Text = tab.Text,
                    AutoSize = false,
                    Location = new Point(x, 0),
                    Size = new Size(tab.Width + 1, 33),
                    Font = Font,
                    ForeColor = TextColor,
                    BackColor = selected ? SelectedTabBack : TabBack,
                    BorderStyle = BorderStyle.FixedSingle,
                    TextAlign = ContentAlignment.MiddleCenter,
                    UseMnemonic = false
                };
                panel.Controls.Add(label);

                // 相邻标签共享一条边框，避免出现双线。
                x += tab.Width;
            }

            return panel;
        }

        private sealed class CommandSpec
        {
            public CommandSpec(string id, string displayText, int width, int iconIndex)
            {
                Id = id;
                DisplayText = displayText;
                Width = width;
                IconIndex = iconIndex;
            }

            public string Id { get; private set; }
            public string DisplayText { get; private set; }
            public int Width { get; private set; }
            public int IconIndex { get; private set; }
        }

        private sealed class TabSpec
        {
            public TabSpec(string text, int width)
            {
                Text = text;
                Width = width;
            }

            public string Text { get; private set; }
            public int Width { get; private set; }
        }

        /// <summary>自绘命令按钮，避免 WinForms Button 自动调整图标和换行导致失真。</summary>
        private sealed class CommandButton : Control
        {
            private readonly Bitmap _icon;
            private bool _hovered;
            private bool _pressed;

            public CommandButton(string text, Bitmap icon)
            {
                Text = text;
                _icon = icon;
                Cursor = Cursors.Hand;
                Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point);
                BackColor = RibbonBack;
                ForeColor = TextColor;
                TabStop = true;

                SetStyle(ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw |
                         ControlStyles.UserPaint, true);
            }

            public bool Highlighted { get; set; }

            protected override void OnMouseEnter(EventArgs e)
            {
                _hovered = true;
                Invalidate();
                base.OnMouseEnter(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                _hovered = false;
                _pressed = false;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    _pressed = true;
                    Focus();
                    Invalidate();
                }

                base.OnMouseDown(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                _pressed = false;
                Invalidate();
                base.OnMouseUp(e);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
                {
                    OnClick(EventArgs.Empty);
                    e.Handled = true;
                }

                base.OnKeyDown(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var active = Highlighted || _hovered || _pressed;
                e.Graphics.Clear(active
                    ? (_pressed ? Color.FromArgb(214, 214, 214) : Color.FromArgb(232, 232, 232))
                    : RibbonBack);

                if (active)
                {
                    using (var pen = new Pen(Border))
                    {
                        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                    }
                }

                if (_icon != null)
                {
                    e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    var iconSize = 28;
                    var iconX = (Width - iconSize) / 2;
                    e.Graphics.DrawImage(_icon, new Rectangle(iconX, 7, iconSize, iconSize));
                }

                var textBounds = new Rectangle(1, 40, Width - 2, Height - 41);
                TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, ForeColor,
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.Top |
                    TextFormatFlags.NoPadding |
                    TextFormatFlags.NoPrefix);
            }
        }
    }
}
