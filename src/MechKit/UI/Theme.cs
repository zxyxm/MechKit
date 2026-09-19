using System.Drawing;
using System.Windows.Forms;

namespace MechKit.UI
{
    /// <summary>统一的界面配色与控件工厂，保证各窗口观感一致。</summary>
    internal static class Theme
    {
        public static readonly Color Accent = Color.FromArgb(15, 108, 189);
        public static readonly Color AccentDark = Color.FromArgb(12, 82, 143);
        public static readonly Color Canvas = Color.FromArgb(246, 247, 249);
        public static readonly Color Surface = Color.White;
        public static readonly Color Border = Color.FromArgb(222, 226, 230);
        public static readonly Color Text = Color.FromArgb(32, 36, 40);
        public static readonly Color Muted = Color.FromArgb(110, 118, 126);
        public static readonly Color Success = Color.FromArgb(16, 124, 65);
        public static readonly Color Danger = Color.FromArgb(180, 45, 55);

        private static readonly string FontFamilyName = ResolveFontFamily();

        public static readonly Font Body = new Font(FontFamilyName, 9f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font BodyBold = new Font(FontFamilyName, 9f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font Small = new Font(FontFamilyName, 8.25f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font Title = new Font(FontFamilyName, 11f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font Mono = new Font("Consolas", 8.5f, FontStyle.Regular, GraphicsUnit.Point);

        private static string ResolveFontFamily()
        {
            foreach (var candidate in new[] { "Microsoft YaHei UI", "微软雅黑", "Microsoft YaHei", "Segoe UI" })
            {
                try
                {
                    using (var family = new FontFamily(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                    // 字体不存在，试下一个
                }
            }

            return FontFamily.GenericSansSerif.Name;
        }

        public static Button CreatePrimaryButton(string text)
        {
            var button = new Button
            {
                Text = text,
                Font = Body,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = Accent,
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = AccentDark;
            button.FlatAppearance.MouseDownBackColor = AccentDark;
            return button;
        }

        public static Button CreateSecondaryButton(string text)
        {
            var button = new Button
            {
                Text = text,
                Font = Body,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = Surface,
                ForeColor = Text,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 242, 246);
            return button;
        }

        public static Button CreateLinkButton(string text)
        {
            var button = new Button
            {
                Text = text,
                Font = Body,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = Surface,
                ForeColor = Accent,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 242, 246);
            return button;
        }

        public static Label CreateLabel(string text, Font font, Color color)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                AutoSize = true,
                BackColor = Color.Transparent
            };
        }

        public static Label CreateFieldLabel(string text)
        {
            return new Label
            {
                Text = text,
                Font = Body,
                ForeColor = Muted,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                // 高分屏下宁可省略号，也不要换行后被行高裁掉
                AutoEllipsis = true,
                BackColor = Color.Transparent
            };
        }

        public static Label CreateValueLabel(string text)
        {
            return new Label
            {
                Text = text,
                Font = Body,
                ForeColor = Text,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                BackColor = Color.Transparent
            };
        }

        public static void StyleGrid(DataGridView grid)
        {
            grid.BackgroundColor = Surface;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.GridColor = Border;
            grid.Font = Body;
            grid.RowHeadersVisible = false;
            grid.AllowUserToResizeRows = false;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 243, 246);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
            grid.ColumnHeadersDefaultCellStyle.Font = BodyBold;
            grid.ColumnHeadersHeight = 28;
            grid.RowTemplate.Height = 26;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 233, 247);
            grid.DefaultCellStyle.SelectionForeColor = Text;
            grid.DefaultCellStyle.Padding = new Padding(2, 0, 2, 0);
        }

        public static TextBox CreateTextBox()
        {
            return new TextBox
            {
                Font = Body,
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        public static void StyleLogBox(TextBox box)
        {
            box.Font = Mono;
            box.ReadOnly = true;
            box.Multiline = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.BackColor = Color.FromArgb(250, 251, 252);
            box.ForeColor = Text;
            box.BorderStyle = BorderStyle.FixedSingle;
        }
    }
}
