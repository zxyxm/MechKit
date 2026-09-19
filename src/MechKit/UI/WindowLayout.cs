using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using MechKit.Core;

namespace MechKit.UI
{
    /// <summary>
    /// 统一管理窗口大小：给足默认尺寸、限制最小尺寸、记住用户调整过的大小与位置。
    /// 布局本身用 Dock/百分比，窗口放大时内容自动跟着放大，不需要滚动。
    /// </summary>
    internal static class WindowLayout
    {
        /// <summary>按 ESC 关闭窗口。KeyPreview 让窗体先拿到按键，单元格/文本框聚焦时也生效。</summary>
        public static void EnableEscapeToClose(Form form)
        {
            form.KeyPreview = true;
            form.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Escape)
                {
                    return;
                }

                e.Handled = true;
                e.SuppressKeyPress = true;

                try
                {
                    form.Close();
                }
                catch
                {
                    // 关闭失败（例如正在批量处理）就保持窗口
                }
            };
        }

        public static void Attach(Form form, AddinSettings settings, Size defaultSize, Size minimumSize)
        {
            // 高分屏自适应：按字体尺寸缩放整套布局。
            // 用 Font 模式（而不是 Dpi）是因为字体本身以磅为单位、会随系统缩放变大，
            // 布局必须按同样的比例放大，否则固定像素的行高会把文字挤掉。
            form.AutoScaleMode = AutoScaleMode.Font;
            form.AutoScaleDimensions = new SizeF(6f, 13f);

            form.StartPosition = FormStartPosition.CenterParent;
            form.ClientSize = defaultSize;
            form.MinimumSize = minimumSize;
            form.MaximizeBox = true;

            EnableEscapeToClose(form);

            if (settings == null)
            {
                return;
            }

            var key = "Window." + form.GetType().Name;

            // 恢复上次的尺寸与位置（若仍在屏幕范围内）
            try
            {
                var saved = settings.Get(key, string.Empty);
                if (!string.IsNullOrEmpty(saved))
                {
                    var parts = saved.Split(',');
                    if (parts.Length == 4)
                    {
                        int left, top, width, height;
                        if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out left) &&
                            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out top) &&
                            int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out width) &&
                            int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out height) &&
                            width >= minimumSize.Width && height >= minimumSize.Height &&
                            left > -width && top > -height &&
                            left < Screen.PrimaryScreen.WorkingArea.Right &&
                            top < Screen.PrimaryScreen.WorkingArea.Bottom)
                        {
                            form.StartPosition = FormStartPosition.Manual;
                            form.Bounds = new Rectangle(left, top, width, height);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("恢复窗口尺寸失败：" + ex.Message);
            }

            form.FormClosed += delegate
            {
                try
                {
                    if (form.WindowState == FormWindowState.Normal)
                    {
                        settings.Set(key, string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}",
                            form.Left, form.Top, form.Width, form.Height));
                        settings.Save();
                    }
                }
                catch
                {
                    // 记不住尺寸不影响使用
                }
            };
        }
    }
}
