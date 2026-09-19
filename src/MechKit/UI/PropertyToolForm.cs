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
    /// <summary>把一个或多个自定义属性批量写入整个文件夹的文档。</summary>
    internal sealed class PropertyToolForm : Form
    {
        private readonly IAddinHost _host;
        private readonly TextBox _folderBox;
        private readonly CheckBox _recursive;
        private readonly CheckBox _overwrite;
        private readonly CheckBox _deleteUnlisted;
        private readonly ComboBox _docTypeBox;
        private readonly DataGridView _grid;
        private readonly TextBox _logBox;
        private readonly ProgressBar _progress;
        private readonly Label _status;
        private readonly Button _startButton;
        private readonly Button _cancelButton;

        private bool _running;
        private bool _cancelRequested;

        public PropertyToolForm(IAddinHost host)
        {
            _host = host;
            _folderBox = Theme.CreateTextBox();
            _recursive = new CheckBox();
            _overwrite = new CheckBox();
            _deleteUnlisted = new CheckBox();
            _docTypeBox = new ComboBox();
            _grid = new DataGridView();
            _logBox = new TextBox();
            _progress = new ProgressBar();
            _status = Theme.CreateValueLabel("就绪");
            _startButton = Theme.CreatePrimaryButton("开始写入");
            _cancelButton = Theme.CreateSecondaryButton("关闭");

            BuildLayout();
            WireEvents();
            LoadDefaults();
        }

        private void BuildLayout()
        {
            Text = "属性批量写入 - " + AddinConstants.Title;
            Font = Theme.Body;
            BackColor = Theme.Canvas;
            StartPosition = FormStartPosition.CenterParent;
            WindowLayout.Attach(this, _host.Settings, new Size(880, 740), new Size(780, 600));

            Controls.Add(BuildBody());
            Controls.Add(BuildLogPanel());
            Controls.Add(BuildFooter());
            Controls.Add(BuildHeader());
        }

        private Control BuildHeader()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Theme.Accent };
            var title = Theme.CreateLabel("属性批量写入", Theme.Title, Color.White);
            title.Location = new Point(14, 9);

            var subtitle = Theme.CreateLabel("把同一组自定义属性写入指定文件夹下的所有文档（文档级属性）",
                Theme.Small, Color.FromArgb(214, 232, 248));
            subtitle.Location = new Point(15, 32);

            panel.Controls.Add(title);
            panel.Controls.Add(subtitle);
            return panel;
        }

        private Control BuildBody()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas, Padding = new Padding(12, 10, 12, 6) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = Theme.Canvas
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            layout.Controls.Add(BuildFolderRow(), 0, 0);
            layout.Controls.Add(BuildOptionRow(), 0, 1);
            layout.Controls.Add(Theme.CreateLabel("要写入的属性（属性名为空的行会被忽略）", Theme.BodyBold, Theme.Text), 0, 2);
            layout.Controls.Add(BuildGrid(), 0, 4);

            panel.Controls.Add(layout);
            return panel;
        }

        private Control BuildFolderRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Theme.Canvas
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86f));

            var caption = Theme.CreateFieldLabel("目标文件夹");
            caption.Dock = DockStyle.Fill;
            caption.ForeColor = Theme.Text;

            _folderBox.Dock = DockStyle.Fill;
            _folderBox.Margin = new Padding(0, 4, 6, 4);

            var browse = Theme.CreateSecondaryButton("浏览…");
            browse.Dock = DockStyle.Fill;
            browse.Margin = new Padding(0, 2, 0, 2);
            browse.Click += OnBrowse;

            row.Controls.Add(caption, 0, 0);
            row.Controls.Add(_folderBox, 1, 0);
            row.Controls.Add(browse, 2, 0);
            return row;
        }

        private Control BuildOptionRow()
        {
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Canvas
            };

            _recursive.Text = "包含子文件夹";
            _overwrite.Text = "覆盖已有属性值";
            _deleteUnlisted.Text = "删除未列出的属性";
            foreach (var box in new[] { _recursive, _overwrite, _deleteUnlisted })
            {
                box.Font = Theme.Body;
                box.ForeColor = Theme.Text;
                box.AutoSize = true;
                box.Margin = new Padding(0, 6, 12, 0);
            }

            var typeCaption = Theme.CreateFieldLabel("文档类型");
            typeCaption.AutoSize = true;
            typeCaption.ForeColor = Theme.Text;
            typeCaption.Margin = new Padding(8, 6, 6, 0);

            _docTypeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _docTypeBox.Font = Theme.Body;
            _docTypeBox.Width = 110;
            _docTypeBox.Margin = new Padding(0, 3, 0, 0);
            _docTypeBox.Items.AddRange(new object[] { "全部", "仅零件", "仅装配体", "仅工程图" });

            row.Controls.Add(_recursive);
            row.Controls.Add(_overwrite);
            row.Controls.Add(_deleteUnlisted);
            row.Controls.Add(typeCaption);
            row.Controls.Add(_docTypeBox);
            return row;
        }

        private Control BuildGrid()
        {
            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = true;
            _grid.AllowUserToDeleteRows = true;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            Theme.StyleGrid(_grid);

            var nameColumn = new DataGridViewTextBoxColumn
            {
                HeaderText = "属性名",
                Width = 180,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };

            var typeColumn = new DataGridViewComboBoxColumn
            {
                HeaderText = "类型",
                Width = 80,
                FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            typeColumn.Items.AddRange("文本", "整数", "小数", "是/否", "日期");

            var valueColumn = new DataGridViewTextBoxColumn
            {
                HeaderText = "值",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };

            _grid.Columns.AddRange(nameColumn, typeColumn, valueColumn);
            return _grid;
        }

        private Control BuildLogPanel()
        {
            var panel = new Panel { Dock = DockStyle.Bottom, Height = 140, BackColor = Theme.Canvas, Padding = new Padding(12, 0, 12, 0) };

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

            var statusHost = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Theme.Canvas
            };
            statusHost.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            statusHost.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            _status.Dock = DockStyle.Fill;
            _status.Margin = new Padding(0);
            _progress.Dock = DockStyle.Fill;
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Margin = new Padding(0, 6, 8, 6);

            statusHost.Controls.Add(_status, 0, 0);
            statusHost.Controls.Add(_progress, 0, 1);

            _startButton.Dock = DockStyle.Fill;
            _startButton.Margin = new Padding(4, 8, 4, 8);
            _cancelButton.Dock = DockStyle.Fill;
            _cancelButton.Margin = new Padding(4, 8, 0, 8);

            layout.Controls.Add(statusHost, 0, 0);
            layout.Controls.Add(_startButton, 1, 0);
            layout.Controls.Add(_cancelButton, 2, 0);

            panel.Controls.Add(layout);
            return panel;
        }

        private void WireEvents()
        {
            _startButton.Click += delegate { StartWriting(); };
            _cancelButton.Click += delegate
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
            };

            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (_running)
                {
                    _cancelRequested = true;
                    e.Cancel = true;
                }
            };
        }

        private void LoadDefaults()
        {
            var settings = _host.Settings;
            _folderBox.Text = settings.PropertyFolder;
            _recursive.Checked = true;
            _overwrite.Checked = true;
            _deleteUnlisted.Checked = false;
            _docTypeBox.SelectedIndex = 0;

            // 预填一行常用属性，方便直接改
            _grid.Rows.Add("描述", "文本", string.Empty);
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择要写入属性的文件夹";
                dialog.ShowNewFolderButton = false;
                if (Directory.Exists(_folderBox.Text))
                {
                    dialog.SelectedPath = _folderBox.Text;
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _folderBox.Text = dialog.SelectedPath;
                }
            }
        }

        private List<CustomProperty> CollectProperties()
        {
            var properties = new List<CustomProperty>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow)
                {
                    continue;
                }

                var name = Convert.ToString(row.Cells[0].Value);
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(name.Trim()))
                {
                    continue;
                }

                name = name.Trim();
                if (!seen.Add(name))
                {
                    continue;
                }

                var type = PropertyTypes.FromDisplayName(Convert.ToString(row.Cells[1].Value));
                var value = Convert.ToString(row.Cells[2].Value) ?? string.Empty;
                properties.Add(new CustomProperty(name, type, value));
            }

            return properties;
        }

        private void StartWriting()
        {
            var folder = _folderBox.Text.Trim();
            if (folder.Length == 0 || !Directory.Exists(folder))
            {
                MessageBox.Show(this, "请选择一个有效的目标文件夹。", AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var properties = CollectProperties();
            if (properties.Count == 0)
            {
                MessageBox.Show(this, "请至少填写一条属性。", AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_deleteUnlisted.Checked &&
                MessageBox.Show(this,
                    "「删除未列出的属性」会移除文档中上表以外的全部自定义属性，且不可撤销。是否继续？",
                    AddinConstants.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            var docTypeFilter = 0;
            switch (_docTypeBox.SelectedIndex)
            {
                case 1:
                    docTypeFilter = SwUtils.DocPart;
                    break;
                case 2:
                    docTypeFilter = SwUtils.DocAssembly;
                    break;
                case 3:
                    docTypeFilter = SwUtils.DocDrawing;
                    break;
            }

            var files = ExportService.CollectFiles(new[] { folder }, _recursive.Checked, docTypeFilter);
            if (files.Count == 0)
            {
                MessageBox.Show(this, "该文件夹下没有匹配的 SOLIDWORKS 文档。", AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var confirm = string.Format("将向 {0} 个文档写入 {1} 条属性，是否继续？", files.Count, properties.Count);
            if (MessageBox.Show(this, confirm, AddinConstants.Title,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            _running = true;
            _cancelRequested = false;
            _startButton.Enabled = false;
            _cancelButton.Text = "取消";
            _logBox.Clear();
            _progress.Minimum = 0;
            _progress.Maximum = files.Count;
            _progress.Value = 0;

            var succeeded = 0;
            var failed = 0;

            try
            {
                for (var i = 0; i < files.Count; i++)
                {
                    if (_cancelRequested)
                    {
                        AppendLog("已取消。");
                        break;
                    }

                    var file = files[i];
                    _status.Text = Path.GetFileName(file);
                    _progress.Value = i + 1;

                    var ok = PropertyService.WriteToFile(_host.SwApp, file, properties,
                        _overwrite.Checked, _deleteUnlisted.Checked, AppendLog);

                    if (ok)
                    {
                        succeeded++;
                        AppendLog("✓ " + Path.GetFileName(file));
                    }
                    else
                    {
                        failed++;
                        AppendLog("✗ " + Path.GetFileName(file) + "：写入失败");
                    }

                    Application.DoEvents();
                }
            }
            catch (Exception ex)
            {
                Log.Error("批量写入属性异常", ex);
                MessageBox.Show(this, "写入过程出错：" + ex.Message, AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _running = false;
                _startButton.Enabled = true;
                _cancelButton.Text = "关闭";
            }

            var summary = string.Format("完成：成功 {0} 个，失败 {1} 个，共 {2} 个文档。", succeeded, failed, files.Count);
            AppendLog(summary);
            _status.Text = summary;
            Log.Info("批量写入属性 " + summary);

            var settings = _host.Settings;
            settings.PropertyFolder = folder;
            settings.Save();

            MessageBox.Show(this, summary, AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            Application.DoEvents();
        }
    }
}
