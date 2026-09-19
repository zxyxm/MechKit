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
        private readonly Control _commandArea;
        private readonly Control _tabStrip;
        private readonly int _contentWidth;

        private const int CommandAreaHeight = 117;
        private const int TabStripHeight = 33;
        private const int ContentHeight = CommandAreaHeight + TabStripHeight;

        public TabPreviewForm(Action<string> onCommand)
        {
            _onCommand = onCommand;

            Text = "MechKit 选项卡离线测试";
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = RibbonBack;
            MaximizeBox = true;

            LoadIcons();

            _canvas = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = RibbonBack,
                AutoScroll = true
            };
            _commandArea = BuildCommandArea();
            _tabStrip = BuildTabStrip();
            _contentWidth = Math.Max(_commandArea.Width, _tabStrip.Width);
            _canvas.AutoScrollMinSize = new Size(_contentWidth, ContentHeight);
            _canvas.Controls.Add(_commandArea);
            _canvas.Controls.Add(_tabStrip);
            Controls.Add(_canvas);

            var workingWidth = Screen.PrimaryScreen == null
                ? 1600
                : Screen.PrimaryScreen.WorkingArea.Width;
            ClientSize = new Size(Math.Min(_contentWidth, Math.Max(900, workingWidth - 80)),
                ContentHeight + SystemInformation.HorizontalScrollBarHeight + 2);

            MechKit.UI.WindowLayout.EnableEscapeToClose(this);
        }

        /// <summary>保存完整 CommandManager 内容，不受当前窗口宽度或滚动位置影响。</summary>
        public void SaveClientImage(string path)
        {
            _commandArea.PerformLayout();
            _tabStrip.PerformLayout();
            using (var bitmap = new Bitmap(_contentWidth, ContentHeight))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(RibbonBack);
                }
                _commandArea.DrawToBitmap(bitmap,
                    new Rectangle(0, 0, _commandArea.Width, _commandArea.Height));
                _tabStrip.DrawToBitmap(bitmap,
                    new Rectangle(0, CommandAreaHeight, _tabStrip.Width, _tabStrip.Height));
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

        /// <summary>固定功能为大按钮；前缀和中间名为上下两行的小按钮。</summary>
        private Control BuildCommandArea()
        {
            var host = new Panel
            {
                Location = Point.Empty,
                Height = CommandAreaHeight,
                BackColor = RibbonBack
            };
            host.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (var pen = new Pen(Border))
                {
                    e.Graphics.DrawLine(pen, 0, host.Height - 1, host.Width, host.Height - 1);
                }
            };

            var leading = new List<CommandSpec>
            {
                new CommandSpec("一键生成BOM", "一键生\r\n成 BOM\r\n表", 76, 0),
                new CommandSpec("明细汇总 / BOM 预览", "明细汇总\r\nBOM预览", 84, 1),
                new CommandSpec("加工件命名规则", "加工件\r\n命名规\r\n则设置", 69, 2)
            };

            var trailing = new List<CommandSpec>
            {
                new CommandSpec("批量导出", "批量\r\n导出", 49, 4),
                new CommandSpec("工具箱面板", "工具\r\n箱面\r\n板", 45, 6),
                new CommandSpec("导出配置", "导出\r\n配置", 52, 4),
                new CommandSpec("设置", "设置", 45, 7)
            };

            var x = 0;
            foreach (var command in leading)
            {
                AddCommandButton(host, command, new Point(x, 0),
                    new Size(command.Width, 117), false);
                x += command.Width;
            }

            // 加工件快捷区固定保留：时间、_→-、装配。
            foreach (var command in new[]
            {
                new CommandSpec("加工件:时间", "时间", 58, 2),
                new CommandSpec("加工件:下划线转换", "_ → -", 66, 3),
                new CommandSpec("装配:装配", "装配", 58, 3)
            })
            {
                AddCommandButton(host, command, new Point(x, 9),
                    new Size(command.Width, 34), true);
                x += command.Width + 2;
            }

            var settings = AddinSettings.Load();
            var visibleLevel2 = new HashSet<string>(
                NamingOptionsFactory.ParsePrefixes(settings.MachinedTabLevel2Values),
                StringComparer.OrdinalIgnoreCase);
            var visibleLevel3 = new HashSet<string>(
                NamingOptionsFactory.ParsePrefixes(settings.MachinedTabLevel3Values),
                StringComparer.OrdinalIgnoreCase);
            var level2 = new List<string>();
            foreach (var value in NamingOptionsFactory.ParsePrefixes(settings.MachinedLevel2Values))
                if (visibleLevel2.Contains(value)) level2.Add(value);
            var level3 = new List<string>();
            foreach (var value in NamingOptionsFactory.ParsePrefixes(settings.MachinedLevel3Values))
                if (visibleLevel3.Contains(value)) level3.Add(value);
            var machinedColumns = Math.Max(
                Math.Min(level2.Count, AddinConstants.MaxMachinedLevel2Commands),
                Math.Min(level3.Count, AddinConstants.MaxMachinedLevel3Commands));
            if (machinedColumns > 0)
            {
                AddLevelLabel(host, "二级", new Point(x, 12));
                AddLevelLabel(host, "三级", new Point(x, 54));
                x += 44;
            }
            for (var column = 0; column < machinedColumns; column++)
            {
                var level2Text = column < level2.Count ? level2[column] : string.Empty;
                var level3Text = column < level3.Count ? level3[column] : string.Empty;
                var width = MeasureLevelColumn(level2Text, level3Text);
                if (level2Text.Length > 0)
                {
                    AddCommandButton(host,
                        new CommandSpec("加工二级:" + level2Text, level2Text, width, 10 + column),
                        new Point(x, 9), new Size(width, 34), true);
                }
                if (level3Text.Length > 0)
                {
                    AddCommandButton(host,
                        new CommandSpec("加工三级:" + level3Text, level3Text, width, 10 + column),
                        new Point(x, 51), new Size(width, 34), true);
                }
                x += width + 2;
            }

            var standardSettings = new CommandSpec("标准件前缀", "标准件\r\n前缀设\r\n置", 63, 3);
            AddCommandButton(host, standardSettings, new Point(x, 0),
                new Size(standardSettings.Width, 117), false);
            x += standardSettings.Width;

            var visiblePrefixes = new HashSet<string>(
                NamingOptionsFactory.ParsePrefixes(settings.StandardTabPrefixes),
                StringComparer.OrdinalIgnoreCase);
            var visibleMiddleNames = new HashSet<string>(
                NamingOptionsFactory.ParsePrefixes(settings.StandardTabMiddleNames),
                StringComparer.OrdinalIgnoreCase);
            var prefixes = new List<string>();
            foreach (var value in NamingOptionsFactory.ParsePrefixes(settings.BomPrefixes))
                if (visiblePrefixes.Contains(value)) prefixes.Add(value);
            var middleNames = new List<string>();
            foreach (var value in NamingOptionsFactory.ParsePrefixes(settings.BomMiddleNames))
                if (visibleMiddleNames.Contains(value)) middleNames.Add(value);
            var prefixCount = Math.Min(prefixes.Count, AddinConstants.MaxPrefixCommands);
            var middleCount = Math.Min(middleNames.Count, AddinConstants.MaxMiddleNameCommands);
            if (prefixCount > 0 || middleCount > 0)
            {
                AddLevelLabel(host, "一级", new Point(x, 12));
                AddLevelLabel(host, "二级", new Point(x, 54));
                x += 44;
            }

            // 一级字段（前缀）排上面一行。
            var standardRowStart = x;
            for (var index = 0; index < prefixCount; index++)
            {
                var width = MeasureLevelColumn(prefixes[index], string.Empty);
                AddCommandButton(host,
                    new CommandSpec("前缀:" + prefixes[index], prefixes[index], width, 10 + index),
                    new Point(x, 9), new Size(width, 34), true);
                x += width + 2;
            }

            // 二级字段（中间名）另起一行排在下面。
            x = standardRowStart;
            for (var index = 0; index < middleCount; index++)
            {
                var width = MeasureLevelColumn(middleNames[index], string.Empty);
                AddCommandButton(host,
                    new CommandSpec("中间:" + middleNames[index], middleNames[index], width, 10 + index),
                    new Point(x, 51), new Size(width, 34), true);
                x += width + 2;
            }

            if (prefixCount > 0 || middleCount > 0)
            {
                x += 2;
            }

            var reference = new CommandSpec("参考件:参考", "参考件", 58, 10);
            AddCommandButton(host, reference, new Point(x, 0),
                new Size(reference.Width, 117), false);
            x += reference.Width;

            foreach (var command in trailing)
            {
                AddCommandButton(host, command, new Point(x, 0),
                    new Size(command.Width, 117), false);
                x += command.Width;
            }

            host.Width = Math.Max(1, x);

            return host;
        }

        private void AddLevelLabel(Control host, string text, Point location)
        {
            host.Controls.Add(new Label
            {
                Text = text,
                Location = location,
                Size = new Size(42, 30),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(Font.FontFamily, 8.5f),
                ForeColor = Color.FromArgb(90, 90, 90),
                BackColor = RibbonBack
            });
        }

        private int MeasureLevelColumn(string first, string second)
        {
            var width = 58;
            if (!string.IsNullOrEmpty(first))
            {
                width = Math.Max(width, TextRenderer.MeasureText(first, Font).Width + 30);
            }
            if (!string.IsNullOrEmpty(second))
            {
                width = Math.Max(width, TextRenderer.MeasureText(second, Font).Width + 30);
            }
            return width;
        }

        private void AddCommandButton(Control host, CommandSpec command, Point location,
            Size size, bool compact)
        {
            var capturedId = command.Id;
            var button = new CommandButton(command.DisplayText,
                command.IconIndex < _icons.Count ? _icons[command.IconIndex] : null, compact)
            {
                Location = location,
                Size = size,
                Highlighted = false
            };
            button.Click += delegate { _onCommand(capturedId); };
            host.Controls.Add(button);
        }

        /// <summary>截图底部的 SOLIDWORKS 选项卡条，MechKit 保持选中。</summary>
        private Control BuildTabStrip()
        {
            var panel = new Panel
            {
                Location = new Point(0, CommandAreaHeight),
                Height = TabStripHeight,
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

            panel.Width = Math.Max(1, x);

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
            private readonly bool _compact;
            private bool _hovered;
            private bool _pressed;

            public CommandButton(string text, Bitmap icon, bool compact)
            {
                Text = text;
                _icon = icon;
                _compact = compact;
                Cursor = Cursors.Hand;
                Font = new Font("Microsoft YaHei UI", compact ? 8.5f : 10.5f,
                    FontStyle.Regular, GraphicsUnit.Point);
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
                    var iconSize = _compact ? 18 : 28;
                    var iconX = _compact ? 5 : (Width - iconSize) / 2;
                    var iconY = _compact ? (Height - iconSize) / 2 : 7;
                    e.Graphics.DrawImage(_icon, new Rectangle(iconX, iconY, iconSize, iconSize));
                }

                var textBounds = _compact
                    ? new Rectangle(26, 0, Width - 28, Height)
                    : new Rectangle(1, 40, Width - 2, Height - 41);
                TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, ForeColor,
                    (_compact ? TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                              : TextFormatFlags.HorizontalCenter | TextFormatFlags.Top) |
                    TextFormatFlags.NoPadding |
                    TextFormatFlags.NoPrefix);
            }
        }
    }
}
