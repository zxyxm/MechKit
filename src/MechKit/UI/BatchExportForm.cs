using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MechKit.Core;
using MechKit.Features;

namespace MechKit.UI
{
    /// <summary>批量导出窗口：选文件 → 选格式 → 输出。</summary>
    internal sealed class BatchExportForm : Form
    {
        private readonly IAddinHost _host;
        private readonly ListBox _fileList;
        private readonly TextBox _outputBox;
        private readonly CheckBox _keepTree;
        private readonly CheckBox _overwrite;
        private readonly CheckBox _report;
        private readonly CheckedListBox _formatList;
        private readonly TextBox _logBox;
        private readonly ProgressBar _progress;
        private readonly Label _status;
        private readonly Button _startButton;
        private readonly Button _cancelButton;

        private string _sourceRoot = string.Empty;
        private bool _running;
        private bool _cancelRequested;

        public BatchExportForm(IAddinHost host)
            : this(host, null)
        {
        }

        /// <summary>
        /// initialFiles 不为空时（BOM 表勾选的行）直接放进待导出列表，
        /// 打开后只需选择导出格式与输出目录。
        /// </summary>
        public BatchExportForm(IAddinHost host, IList<string> initialFiles)
        {
            _host = host;
            _fileList = new ListBox();
            _outputBox = Theme.CreateTextBox();
            _keepTree = new CheckBox();
            _overwrite = new CheckBox();
            _report = new CheckBox();
            _formatList = new CheckedListBox();
            _logBox = new TextBox();
            _progress = new ProgressBar();
            _status = Theme.CreateValueLabel("就绪");
            _startButton = Theme.CreatePrimaryButton("开始导出");
            _cancelButton = Theme.CreateSecondaryButton("关闭");

            BuildLayout();
            WireEvents();
            LoadDefaults();
            AddInitialFiles(initialFiles);
        }

        private void AddInitialFiles(IList<string> files)
        {
            if (files == null)
            {
                return;
            }

            foreach (var file in files)
            {
                if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                {
                    continue;
                }
                if (_fileList.Items.Contains(file))
                {
                    continue;
                }

                _fileList.Items.Add(file);
            }

            if (_fileList.Items.Count == 0)
            {
                return;
            }

            // 以带入文件的目录作为“保持目录结构”的相对根。
            try
            {
                _sourceRoot = Path.GetDirectoryName((string)_fileList.Items[0]) ?? string.Empty;
            }
            catch
            {
                _sourceRoot = string.Empty;
            }

            _status.Text = string.Format("已从 BOM 带入 {0} 个文件，选好格式后点“开始导出”。",
                _fileList.Items.Count);
        }

        private void BuildLayout()
        {
            Text = "批量导出 - " + AddinConstants.Title;
            Font = Theme.Body;
            BackColor = Theme.Canvas;
            StartPosition = FormStartPosition.CenterParent;
            WindowLayout.Attach(this, _host.Settings, new Size(980, 780), new Size(840, 620));

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Theme.Canvas,
                Padding = new Padding(12, 10, 12, 6)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));
            body.Controls.Add(BuildFilePanel(), 0, 0);
            body.Controls.Add(BuildOptionPanel(), 1, 0);

            Controls.Add(body);
            Controls.Add(BuildLogPanel());
            Controls.Add(BuildFooter());
            Controls.Add(BuildHeader());
        }

        private Control BuildHeader()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Theme.Accent };
            var title = Theme.CreateLabel("批量导出", Theme.Title, Color.White);
            title.Location = new Point(14, 9);

            var subtitle = Theme.CreateLabel(
                "工程图导出 PDF / DWG / DXF，零件与装配体导出 STEP / IGES / STL",
                Theme.Small, Color.FromArgb(214, 232, 248));
            subtitle.Location = new Point(15, 32);

            panel.Controls.Add(title);
            panel.Controls.Add(subtitle);
            return panel;
        }

        private Control BuildFilePanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(10, 8, 10, 8), Margin = new Padding(0, 0, 6, 0) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Theme.Surface
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));

            layout.Controls.Add(Theme.CreateLabel("待导出文件", Theme.BodyBold, Theme.Text), 0, 0);

            _fileList.Dock = DockStyle.Fill;
            _fileList.Font = Theme.Body;
            _fileList.SelectionMode = SelectionMode.MultiExtended;
            _fileList.IntegralHeight = false;
            _fileList.HorizontalScrollbar = true;
            _fileList.BorderStyle = BorderStyle.FixedSingle;
            layout.Controls.Add(_fileList, 0, 1);

            var buttons = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Theme.Surface
            };
            for (var i = 0; i < 3; i++)
            {
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            }

            var addFiles = Theme.CreateSecondaryButton("添加文件…");
            var addFolder = Theme.CreateSecondaryButton("添加文件夹…");
            var clear = Theme.CreateSecondaryButton("清空");
            foreach (var button in new[] { addFiles, addFolder, clear })
            {
                button.Dock = DockStyle.Fill;
                button.Margin = new Padding(2, 5, 2, 5);
            }

            addFiles.Click += OnAddFiles;
            addFolder.Click += OnAddFolder;
            clear.Click += delegate
            {
                _fileList.Items.Clear();
                _sourceRoot = string.Empty;
                UpdateStatus();
            };

            buttons.Controls.Add(addFiles, 0, 0);
            buttons.Controls.Add(addFolder, 1, 0);
            buttons.Controls.Add(clear, 2, 0);
            layout.Controls.Add(buttons, 0, 2);

            panel.Controls.Add(layout);
            return panel;
        }

        private Control BuildOptionPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(10, 8, 10, 8), Margin = new Padding(6, 0, 0, 0) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 8,
                BackColor = Theme.Surface
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));

            layout.Controls.Add(Theme.CreateLabel("输出目录", Theme.BodyBold, Theme.Text), 0, 0);

            var outputRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Theme.Surface
            };
            outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80f));

            _outputBox.Dock = DockStyle.Fill;
            _outputBox.Margin = new Padding(0, 3, 4, 3);
            var browse = Theme.CreateSecondaryButton("浏览…");
            browse.Dock = DockStyle.Fill;
            browse.Margin = new Padding(2, 1, 0, 1);
            browse.Click += OnBrowseOutput;

            outputRow.Controls.Add(_outputBox, 0, 0);
            outputRow.Controls.Add(browse, 1, 0);
            layout.Controls.Add(outputRow, 0, 1);

            _keepTree.Text = "保持与源文件相同的目录结构";
            _overwrite.Text = "覆盖已存在的同名文件";
            _report.Text = "导出完成后生成 CSV 报告";
            foreach (var box in new[] { _keepTree, _overwrite, _report })
            {
                box.Font = Theme.Body;
                box.ForeColor = Theme.Text;
                box.AutoSize = true;
                box.Dock = DockStyle.Fill;
            }

            var options = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Theme.Surface
            };
            options.Controls.Add(_keepTree);
            options.Controls.Add(_overwrite);
            options.Controls.Add(_report);
            layout.Controls.Add(options, 0, 2);

            layout.Controls.Add(Theme.CreateLabel("导出格式", Theme.BodyBold, Theme.Text), 0, 3);

            _formatList.Dock = DockStyle.Fill;
            _formatList.Font = Theme.Body;
            _formatList.CheckOnClick = true;
            _formatList.BorderStyle = BorderStyle.FixedSingle;
            foreach (var format in ExportFormats.All)
            {
                _formatList.Items.Add(format, false);
            }

            layout.Controls.Add(_formatList, 0, 4);

            var hint = Theme.CreateLabel("提示：DWG / DXF 仅适用于工程图，STEP / IGES / STL 仅适用于模型。",
                Theme.Small, Theme.Muted);
            hint.Dock = DockStyle.Fill;
            layout.Controls.Add(hint, 0, 5);

            var hint2 = Theme.CreateLabel("导出格式的细节选项（线型、图层、单位等）沿用 SOLIDWORKS 当前设置。",
                Theme.Small, Theme.Muted);
            hint2.Dock = DockStyle.Fill;
            layout.Controls.Add(hint2, 0, 6);

            panel.Controls.Add(layout);
            return panel;
        }

        private Control BuildLogPanel()
        {
            var panel = new Panel { Dock = DockStyle.Bottom, Height = 150, BackColor = Theme.Canvas, Padding = new Padding(12, 0, 12, 0) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Theme.Canvas
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            layout.Controls.Add(Theme.CreateLabel("处理日志", Theme.BodyBold, Theme.Text), 0, 0);
            Theme.StyleLogBox(_logBox);
            _logBox.Dock = DockStyle.Fill;
            layout.Controls.Add(_logBox, 0, 1);

            panel.Controls.Add(layout);
            return panel;
        }

        private Control BuildFooter()
        {
            var panel = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Canvas, Padding = new Padding(12, 8, 12, 8) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Theme.Canvas
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));

            _progress.Dock = DockStyle.Fill;
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Margin = new Padding(0, 6, 8, 6);

            _status.Dock = DockStyle.Fill;
            _status.Margin = new Padding(0);

            var statusHost = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Theme.Canvas
            };
            statusHost.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            statusHost.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            statusHost.Controls.Add(_status, 0, 0);
            statusHost.Controls.Add(_progress, 0, 1);

            layout.Controls.Add(statusHost, 0, 0);

            _startButton.Dock = DockStyle.Fill;
            _startButton.Margin = new Padding(4, 8, 4, 8);
            _cancelButton.Dock = DockStyle.Fill;
            _cancelButton.Margin = new Padding(4, 8, 0, 8);

            layout.Controls.Add(_startButton, 1, 0);
            layout.Controls.Add(_cancelButton, 2, 0);

            panel.Controls.Add(layout);
            return panel;
        }

        private void WireEvents()
        {
            _startButton.Click += delegate { StartExport(); };
            _cancelButton.Click += OnCancelClick;
            FormClosing += OnFormClosing;
        }

        private void LoadDefaults()
        {
            var settings = _host.Settings;
            _outputBox.Text = settings.OutputFolder;
            _keepTree.Checked = settings.ExportKeepTree;
            _overwrite.Checked = true;
            _report.Checked = true;

            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in (settings.ExportFormats ?? string.Empty).Split(','))
            {
                if (!string.IsNullOrEmpty(item))
                {
                    selected.Add(item.Trim());
                }
            }

            if (selected.Count == 0)
            {
                selected.Add(".pdf");
            }

            for (var i = 0; i < _formatList.Items.Count; i++)
            {
                var format = (ExportFormat)_formatList.Items[i];
                _formatList.SetItemChecked(i, selected.Contains(format.Extension));
            }

            var folder = settings.SourceFolder;
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                AddFolder(folder, settings.ExportRecursive);
            }

            UpdateStatus();
        }

        private void OnAddFiles(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择要导出的 SOLIDWORKS 文件";
                dialog.Filter = "SOLIDWORKS 文件|*.sldprt;*.sldasm;*.slddrw|所有文件|*.*";
                dialog.Multiselect = true;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                foreach (var file in dialog.FileNames)
                {
                    AddFile(file);
                }

                if (string.IsNullOrEmpty(_sourceRoot) && dialog.FileNames.Length > 0)
                {
                    _sourceRoot = Path.GetDirectoryName(dialog.FileNames[0]) ?? string.Empty;
                }

                UpdateStatus();
            }
        }

        private void OnAddFolder(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择包含 SOLIDWORKS 文件的文件夹";
                dialog.ShowNewFolderButton = false;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var recursive = MessageBox.Show(this,
                    "是否包含子文件夹？", AddinConstants.Title,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

                AddFolder(dialog.SelectedPath, recursive);
                UpdateStatus();
            }
        }

        private void AddFolder(string folder, bool recursive)
        {
            _sourceRoot = folder;
            var files = ExportService.CollectFiles(new[] { folder }, recursive, 0);
            foreach (var file in files)
            {
                AddFile(file);
            }

            AppendLog(string.Format("已添加 {0}：{1} 个文件", folder, files.Count));
        }

        private void AddFile(string file)
        {
            if (!SwUtils.IsSolidWorksFile(file))
            {
                return;
            }

            foreach (var existing in _fileList.Items)
            {
                if (string.Equals((string)existing, file, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            _fileList.Items.Add(file);
        }

        private void OnBrowseOutput(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择导出文件的存放目录";
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(_outputBox.Text))
                {
                    dialog.SelectedPath = _outputBox.Text;
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _outputBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void OnCancelClick(object sender, EventArgs e)
        {
            if (_running)
            {
                _cancelRequested = true;
                _status.Text = "正在取消…";
            }
            else
            {
                Close();
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_running)
            {
                _cancelRequested = true;
                e.Cancel = true;
            }
        }

        private void StartExport()
        {
            var files = new List<string>();
            foreach (var item in _fileList.Items)
            {
                files.Add((string)item);
            }

            if (files.Count == 0)
            {
                MessageBox.Show(this, "请先添加要导出的文件或文件夹。", AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var formats = new List<ExportFormat>();
            foreach (var item in _formatList.CheckedItems)
            {
                formats.Add((ExportFormat)item);
            }

            if (formats.Count == 0)
            {
                MessageBox.Show(this, "请至少选择一种导出格式。", AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var output = _outputBox.Text.Trim();
            if (output.Length == 0)
            {
                MessageBox.Show(this, "请指定输出目录。", AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var options = new BatchExportOptions
            {
                SourceRoot = _sourceRoot,
                OutputFolder = output,
                KeepTree = _keepTree.Checked,
                Overwrite = _overwrite.Checked,
                CreateReport = _report.Checked
            };
            options.Files.AddRange(files);
            options.Formats.AddRange(formats);

            _running = true;
            _cancelRequested = false;
            _startButton.Enabled = false;
            _cancelButton.Text = "取消";
            _progress.Minimum = 0;
            _progress.Maximum = files.Count;
            _progress.Value = 0;
            _logBox.Clear();
            AppendLog(string.Format("开始导出 {0} 个文件…", files.Count));

            try
            {
                var report = ExportService.Run(_host.SwApp, options, AppendLog,
                    () => _cancelRequested,
                    (current, total) => _progress.Value = Math.Min(current, _progress.Maximum));

                AppendLog(report.Summary());
                _status.Text = report.Summary();

                SaveSettings(options);

                var message = report.Summary();
                if (!string.IsNullOrEmpty(report.ReportPath))
                {
                    message += Environment.NewLine + Environment.NewLine + "报告：" + report.ReportPath;
                }

                if (MessageBox.Show(this, message + Environment.NewLine + Environment.NewLine + "是否打开输出目录？",
                        AddinConstants.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    OpenFolder(output);
                }
            }
            catch (Exception ex)
            {
                Log.Error("批量导出异常", ex);
                MessageBox.Show(this, "导出过程出错：" + ex.Message, AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _running = false;
                _startButton.Enabled = true;
                _cancelButton.Text = "关闭";
            }
        }

        private void SaveSettings(BatchExportOptions options)
        {
            var settings = _host.Settings;
            settings.OutputFolder = options.OutputFolder;
            settings.SourceFolder = _sourceRoot;
            settings.ExportKeepTree = options.KeepTree;

            var extensions = new List<string>();
            foreach (var format in options.Formats)
            {
                extensions.Add(format.Extension);
            }

            settings.ExportFormats = string.Join(",", extensions.ToArray());
            settings.Save();
        }

        private void UpdateStatus()
        {
            _status.Text = ExportService.DescribeCount(_fileList.Items.Count);
        }

        private void AppendLog(string message)
        {
            if (_logBox.IsDisposed)
            {
                return;
            }

            _logBox.AppendText(message + Environment.NewLine);
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();

            // SOLIDWORKS API 必须在本线程调用，用消息泵保持界面可响应
            Application.DoEvents();
        }

        private void OpenFolder(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Process.Start("explorer.exe", folder);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("打开输出目录失败：" + ex.Message);
            }
        }
    }
}
