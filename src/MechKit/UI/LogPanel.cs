using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MechKit.Core;

namespace MechKit.UI
{
    internal sealed class LogPanel : UserControl
    {
        private readonly ComboBox _source = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
        private readonly CheckBox _follow = new CheckBox { Text = "自动刷新", Checked = true, AutoSize = true };
        private readonly TextBox _text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
        private readonly Label _status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "正在读取日志…" };
        private readonly Timer _timer = new Timer { Interval = 1000 };
        private bool _reading;
        private string _shownPath;

        internal LogPanel()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Canvas;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(6) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            _source.Items.AddRange(new object[] { "操作 / 卡顿", "运行 / 错误" });
            _source.SelectedIndex = 0;
            var refresh = Theme.CreateSecondaryButton("刷新");
            refresh.AutoSize = true;
            refresh.Click += async delegate { await RefreshLog(); };
            var folder = Theme.CreateSecondaryButton("打开日志目录");
            folder.AutoSize = true;
            folder.Click += delegate
            {
                try
                {
                    AppPaths.Ensure(AppPaths.Logs);
                    WindowLayout.OpenExternal(new ProcessStartInfo("explorer.exe", AppPaths.Logs) { UseShellExecute = true });
                }
                catch (Exception ex) { _status.Text = "打开失败：" + ex.Message; }
            };
            toolbar.Controls.AddRange(new Control[] { _source, _follow, refresh, folder });
            layout.Controls.Add(toolbar, 0, 0);
            layout.Controls.Add(_status, 0, 1);
            layout.Controls.Add(_text, 0, 2);
            Controls.Add(layout);
            _source.SelectedIndexChanged += async delegate { await RefreshLog(); };
            VisibleChanged += async delegate { if (Visible) await RefreshLog(); };
            _timer.Tick += async delegate { if (Visible && _follow.Checked) await RefreshLog(); };
            _timer.Start();
        }

        private async Task RefreshLog()
        {
            if (_reading || IsDisposed) return;
            _reading = true;
            int source = _source.SelectedIndex;
            string path = source == 0 ? OperationTrace.CurrentFile : Log.CurrentLogFile;
            try
            {
                // Bounded disk reads stay off the SW UI thread. No COM is used.
                string content = string.IsNullOrEmpty(path) ? "操作记录器未启用，请保存工作并重启 SOLIDWORKS。" : await Task.Run(() => ReadTail(path));
                if (IsDisposed || Disposing || source != _source.SelectedIndex) return;
                _status.Text = string.IsNullOrEmpty(path) ? "操作记录器未启用" : Path.GetFileName(path) + " · 最近 64 KB";
                if (_shownPath != path || _text.Text != content)
                {
                    _shownPath = path;
                    _text.Text = content;
                    _text.SelectionStart = _text.TextLength;
                    _text.ScrollToCaret();
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing) _status.Text = "读取日志失败：" + ex.Message;
            }
            finally { _reading = false; }
        }

        internal static string ReadTail(string path)
        {
            if (!File.Exists(path)) return "暂无日志，执行操作后自动显示。";
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long start = Math.Max(0, stream.Length - 65536);
                stream.Position = start;
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    if (start > 0) reader.ReadLine(); // Skip partial UTF-8 / partial first line.
                    var buffer = new char[65536];
                    int count = reader.ReadBlock(buffer, 0, buffer.Length);
                    return new string(buffer, 0, count);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
