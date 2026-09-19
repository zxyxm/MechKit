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
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder text, int count);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const uint GaRoot = 2;
        private const uint GaParent = 1;

        /// <summary>
        /// 鼠标当前是否落在 SOLIDWORKS 的三维图形区上：
        /// ① 点在当前文档视图窗口的矩形内；或
        /// ② 鼠标下的窗口是 SOLIDWORKS 的视图窗口（MFC 的 AfxFrameOrViewXXX）。
        /// </summary>
        public static bool IsCursorOverGraphicsArea(IntPtr viewHandle, IntPtr solidWorksWindow)
        {
            try
            {
                POINT point;
                RECT main;
                if (!GetCursorPos(out point) || !GetWindowRect(solidWorksWindow, out main))
                {
                    return false;
                }

                var mainWidth = main.Right - main.Left;
                if (viewHandle != IntPtr.Zero)
                {
                    RECT rect;
                    if (GetWindowRect(viewHandle, out rect))
                    {
                        var viewWidth = rect.Right - rect.Left;
                        // 只有拿到“比整个客户区小”的具体视图时才用它判断；有些版本会直接
                        // 返回整个客户区，那种情况退回到类名判断。
                        if (viewWidth > 0 && viewWidth < mainWidth &&
                            point.X >= rect.Left && point.X <= rect.Right &&
                            point.Y >= rect.Top && point.Y <= rect.Bottom)
                        {
                            return true;
                        }
                    }
                }

                var target = WindowFromPoint(point);
                if (target == IntPtr.Zero)
                {
                    return false;
                }

                if (GetAncestor(target, GaRoot) != solidWorksWindow)
                {
                    return false;   // 鼠标不在 SOLIDWORKS 主窗口里
                }

                var name = new System.Text.StringBuilder(256);
                GetClassName(target, name, name.Capacity);
                var className = name.ToString();

                // 停靠面板、工具栏、命令管理器、树 / 列表 / 按钮等一律不算图形区。
                foreach (var prefix in new[]
                {
                    "XTPDockingPane", "AfxControlBar", "ToolbarWindow32", "MsoCommandBar",
                    "SysTreeView32", "SysTabControl32", "SysListView32", "SysHeader32",
                    "Button", "Static", "Edit", "ComboBox", "msctls_statusbar32"
                })
                {
                    if (className.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                if (className.StartsWith("AfxFrameOrView", StringComparison.OrdinalIgnoreCase))
                {
                    // 面板视图通常很窄；图形区占客户区大部分宽度。
                    RECT rect;
                    if (!GetWindowRect(target, out rect) || (rect.Right - rect.Left) < mainWidth / 2)
                    {
                        return false;
                    }
                }

                // 只有落在 SOLIDWORKS 的 MDI 图形客户区里才算图形区：
                // 菜单栏、命令管理器、状态栏等虽然也在主窗口里，但不在这个客户区内。
                return IsInsideGraphicsClient(target);
            }
            catch (Exception ex)
            {
                Log.Warn("判断鼠标是否在图形区失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>窗口是否位于 SOLIDWORKS 的 MDI 图形客户区（swMdiClient / MDIClient）之内。</summary>
        private static bool IsInsideGraphicsClient(IntPtr hwnd)
        {
            var current = hwnd;
            for (var depth = 0; depth < 10 && current != IntPtr.Zero; depth++)
            {
                var name = new System.Text.StringBuilder(256);
                GetClassName(current, name, name.Capacity);
                var className = name.ToString();
                if (className.StartsWith("swMdiClient", StringComparison.OrdinalIgnoreCase) ||
                    className.StartsWith("MDIClient", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                current = GetAncestor(current, GaParent);
            }

            return false;
        }

        /// <summary>
        /// 注册“点了 SOLIDWORKS 图形区就把窗口沉到下一层”：窗口失去焦点、前台窗口是
        /// SOLIDWORKS、并且鼠标正落在图形区上时才置底；点菜单、工具栏、特征树、
        /// 命令管理器等其他区域不会让窗口下沉，切到别的程序也不动。
        /// 再从 MechKit 菜单打开（或点一下窗口）即可回到最前面。
        /// </summary>
        public static void SendToBackWhenGraphicsAreaClicked(Form form, IntPtr solidWorksWindow,
            Func<IntPtr> activeViewHandle)
        {
            if (form == null || solidWorksWindow == IntPtr.Zero)
            {
                return;
            }

            form.Deactivate += delegate
            {
                try
                {
                    if (form.IsDisposed || !form.Visible || GetForegroundWindow() != solidWorksWindow)
                    {
                        return;
                    }

                    var viewHandle = activeViewHandle == null ? IntPtr.Zero : activeViewHandle();
                    if (!IsCursorOverGraphicsArea(viewHandle, solidWorksWindow))
                    {
                        return;
                    }

                    // 延迟到消息处理完再置底，避免和激活流程打架。
                    form.BeginInvoke(new Action(delegate
                    {
                        if (!form.IsDisposed && form.Visible)
                        {
                            form.SendToBack();
                        }
                    }));
                }
                catch (Exception ex)
                {
                    Log.Warn("把 MechKit 窗口沉到下一层失败：" + ex.Message);
                }
            };
        }

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

            var workingArea = Screen.PrimaryScreen.WorkingArea;
            var availableWidth = Math.Max(640, workingArea.Width - 48);
            var availableHeight = Math.Max(480, workingArea.Height - 72);
            var fittedDefault = new Size(
                Math.Min(defaultSize.Width, availableWidth),
                Math.Min(defaultSize.Height, availableHeight));
            var fittedMinimum = new Size(
                Math.Min(minimumSize.Width, availableWidth),
                Math.Min(minimumSize.Height, availableHeight));

            form.StartPosition = FormStartPosition.CenterParent;
            form.ClientSize = fittedDefault;
            form.MinimumSize = fittedMinimum;
            form.MaximizeBox = true;
            form.SizeGripStyle = SizeGripStyle.Show;

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
                            width >= fittedMinimum.Width && height >= fittedMinimum.Height &&
                            IsVisibleOnAnyScreen(new Rectangle(left, top, width, height)))
                        {
                            form.StartPosition = FormStartPosition.Manual;
                            form.Bounds = ClampToWorkingArea(
                                new Rectangle(left, top, width, height));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("恢复窗口尺寸失败：" + ex.Message);
            }

            // 字体/DPI 缩放发生在创建句柄之后，再兜底一次，避免窗口超出屏幕、
            // 底部按钮或右侧字段被裁掉。所有功能窗口统一受此规则约束。
            form.Shown += delegate
            {
                try
                {
                    form.Bounds = ClampToWorkingArea(form.Bounds);
                    FitTextRows(form);
                }
                catch
                {
                    // 不影响窗口显示
                }
            };

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

        /// <summary>
        /// 给嵌入式控件（例如 SOLIDWORKS 任务面板）启用与窗体相同的文字防裁切处理。
        /// </summary>
        public static void AttachTextSafety(Control root)
        {
            if (root == null)
            {
                return;
            }

            root.HandleCreated += delegate
            {
                try
                {
                    root.BeginInvoke(new Action(delegate { FitTextRows(root); }));
                }
                catch
                {
                    // 控件可能已在关闭，不影响主程序。
                }
            };
        }

        /// <summary>
        /// WinForms 的 TableLayoutPanel 不会根据中文换行自动增大 Absolute 行高。
        /// 在布局完成后按实际字体测量每一行，必要时只增高对应行，避免标签、复选框
        /// 和按钮文字被下一行遮住。嵌套表格从内向外处理。
        /// </summary>
        private static void FitTextRows(Control root)
        {
            if (root == null || root.IsDisposed)
            {
                return;
            }

            foreach (Control child in root.Controls)
            {
                FitTextRows(child);
            }

            var table = root as TableLayoutPanel;
            if (table == null || table.RowStyles.Count == 0)
            {
                return;
            }

            var rowHeights = table.GetRowHeights();
            for (var row = 0; row < table.RowStyles.Count && row < rowHeights.Length; row++)
            {
                var style = table.RowStyles[row];
                if (style.SizeType != SizeType.Absolute)
                {
                    continue;
                }

                var required = 0;
                foreach (Control child in table.Controls)
                {
                    var position = table.GetCellPosition(child);
                    if (position.Row != row || table.GetRowSpan(child) != 1)
                    {
                        continue;
                    }

                    required = Math.Max(required, RequiredControlHeight(child) + child.Margin.Vertical);
                }

                if (required > rowHeights[row])
                {
                    // 限制单次扩张，异常长的说明文字交给所在滚动面板处理。
                    var growth = Math.Min(96, required - rowHeights[row]);
                    style.Height += growth;
                }
            }
        }

        private static int RequiredControlHeight(Control control)
        {
            var text = control.Text ?? string.Empty;
            if (text.Length == 0)
            {
                return control.MinimumSize.Height;
            }

            if (!(control is Label) && !(control is CheckBox) &&
                !(control is RadioButton) && !(control is Button))
            {
                return control.MinimumSize.Height;
            }

            var width = Math.Max(24, control.ClientSize.Width - control.Padding.Horizontal);
            if (control is CheckBox || control is RadioButton)
            {
                width = Math.Max(24, width - 22);
            }

            var flags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl |
                        TextFormatFlags.NoPrefix;
            var measured = TextRenderer.MeasureText(text, control.Font,
                new Size(width, int.MaxValue), flags);
            var verticalPadding = control.Padding.Vertical + (control is Button ? 10 : 4);
            return measured.Height + verticalPadding;
        }

        private static bool IsVisibleOnAnyScreen(Rectangle bounds)
        {
            foreach (var screen in Screen.AllScreens)
            {
                var visible = Rectangle.Intersect(screen.WorkingArea, bounds);
                if (visible.Width >= 80 && visible.Height >= 60)
                {
                    return true;
                }
            }
            return false;
        }

        private static Rectangle ClampToWorkingArea(Rectangle bounds)
        {
            var screen = Screen.FromRectangle(bounds);
            var area = screen.WorkingArea;
            var width = Math.Min(bounds.Width, area.Width - 16);
            var height = Math.Min(bounds.Height, area.Height - 16);
            var left = Math.Max(area.Left + 8,
                Math.Min(bounds.Left, area.Right - width - 8));
            var top = Math.Max(area.Top + 8,
                Math.Min(bounds.Top, area.Bottom - height - 8));
            return new Rectangle(left, top, Math.Max(320, width), Math.Max(240, height));
        }
    }
}
