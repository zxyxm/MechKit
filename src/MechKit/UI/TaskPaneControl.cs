using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using MechKit.Core;
using MechKit.Features;

namespace MechKit.UI
{
    /// <summary>
    /// 任务面板：显示当前文档的图号 / 材料，装配体给出加工件数量概览，
    /// 并提供自定义属性的快速编辑。
    /// </summary>
    internal sealed class TaskPaneControl : UserControl
    {
        private readonly IAddinHost _host;
        private readonly Label _fileValue;
        private readonly Label _typeValue;
        private readonly Label _partNumberValue;
        private readonly Label _materialValue;
        private readonly Label _quantityValue;
        private readonly DataGridView _grid;
        private readonly CheckBox _syncDelete;
        private readonly Label _status;
        private readonly Button _applyButton;
        private readonly Button _refreshButton;
        private readonly Button _addButton;
        private readonly Button _removeButton;
        private readonly Dictionary<string, CustomProperty> _original =
            new Dictionary<string, CustomProperty>(StringComparer.OrdinalIgnoreCase);

        public TaskPaneControl(IAddinHost host)
        {
            _host = host;

            _fileValue = Theme.CreateValueLabel(string.Empty);
            _typeValue = Theme.CreateValueLabel(string.Empty);
            _partNumberValue = Theme.CreateValueLabel(string.Empty);
            _materialValue = Theme.CreateValueLabel(string.Empty);
            _quantityValue = Theme.CreateValueLabel(string.Empty);
            _grid = new DataGridView();
            _syncDelete = new CheckBox();
            _status = Theme.CreateValueLabel("就绪 · 点击此处查看日志");
            _applyButton = Theme.CreatePrimaryButton("应用");
            _refreshButton = Theme.CreateSecondaryButton("刷新");
            _addButton = Theme.CreateSecondaryButton("新增行");
            _removeButton = Theme.CreateSecondaryButton("删除行");

            Dock = DockStyle.Fill;
            BackColor = Theme.Canvas;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);

            BuildLayout();
            WireEvents();
            Log.Message += OnLogMessage;
            RefreshDocument(true);
        }

        /// <summary>重新读取当前文档信息；deep=true 时额外统计装配体加工件数量。</summary>
        public void RefreshDocument()
        {
            RefreshDocument(false);
        }

        public void RefreshDocument(bool deep)
        {
            if (IsDisposed)
            {
                return;
            }

            var swApp = _host.SwApp;
            var doc = SwUtils.ActiveDoc(swApp);

            try
            {
                _grid.Rows.Clear();
                _original.Clear();

                if (doc == null)
                {
                    _fileValue.Text = "（未打开文档）";
                    _typeValue.Text = "-";
                    _partNumberValue.Text = "-";
                    _materialValue.Text = "-";
                    _quantityValue.Text = "-";
                }
                else
                {
                    var path = doc.GetPathName();
                    _fileValue.Text = string.IsNullOrEmpty(path) ? doc.GetTitle() : System.IO.Path.GetFileName(path);
                    _typeValue.Text = SwUtils.DocTypeName(doc.GetType());

                    var options = BuildOptions();
                    var row = PartListService.FromPart(swApp, doc, options);
                    _partNumberValue.Text = row == null || string.IsNullOrEmpty(row.PartNumber) ? "-" : row.PartNumber;
                    _materialValue.Text = row == null || string.IsNullOrEmpty(row.Material) ? "-" : row.Material;
                    _quantityValue.Text = string.IsNullOrEmpty(SwUtils.ActiveConfigurationName(doc))
                        ? "-"
                        : SwUtils.ActiveConfigurationName(doc);

                    foreach (var property in PropertyService.Read(doc, PropertyService.DocumentLevelConfiguration))
                    {
                        _original[property.Name] = property;

                        var index = _grid.Rows.Add();
                        var gridRow = _grid.Rows[index];
                        gridRow.Cells[0].Value = property.Name;
                        gridRow.Cells[1].Value = PropertyTypes.ToDisplayName(property.Type);
                        gridRow.Cells[2].Value = property.ResolvedValue;
                    }

                    if (deep && doc.GetType() == SwUtils.DocAssembly)
                    {
                        SummarizeAssembly(swApp, doc, options);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("刷新任务面板失败", ex);
            }

            var hasDocument = doc != null;
            _grid.Enabled = hasDocument;
            _applyButton.Enabled = hasDocument;
            _addButton.Enabled = hasDocument;
            _removeButton.Enabled = hasDocument;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Log.Message -= OnLogMessage;
            }

            base.Dispose(disposing);
        }

        private PartListOptions BuildOptions()
        {
            var settings = _host.Settings;
            var options = new PartListOptions
            {
                OnlyMachined = false,
                ExcludeToolbox = true,
                DetectVendorParts = settings.DetectVendorParts,
                ExcludeSuppressed = true,
                ReadCustomProperties = settings.PartListReadProperties
            };

            options.Naming = NamingOptionsFactory.FromSettings(settings);
            // 任务面板只看当前文档，不做 BOM 收录过滤
            options.Naming.RequireBomPattern = false;
            return options;
        }

        private void SummarizeAssembly(SolidWorks.Interop.sldworks.ISldWorks swApp,
            SolidWorks.Interop.sldworks.ModelDoc2 doc, PartListOptions options)
        {
            var assembly = doc as SolidWorks.Interop.sldworks.AssemblyDoc;
            if (assembly == null)
            {
                return;
            }

            // 概览只做快速遍历，不打开零件文件
            var quick = new PartListOptions
            {
                OnlyMachined = true,
                ExcludeToolbox = options.ExcludeToolbox,
                DetectVendorParts = options.DetectVendorParts,
                ExcludeSuppressed = true,
                ReadCustomProperties = false
            };
            quick.Naming.Source = options.Naming.Source;
            quick.Naming.Cut = options.Naming.Cut;
            quick.Naming.Pattern = options.Naming.Pattern;
            quick.Naming.Material = options.Naming.Material;
            quick.Naming.UseNameSegments = options.Naming.UseNameSegments;
            quick.Naming.SegmentSeparator = options.Naming.SegmentSeparator;
            quick.Naming.NameSegment = options.Naming.NameSegment;
            quick.Naming.MaterialSegment = options.Naming.MaterialSegment;
            quick.Naming.MachinedSegments = options.Naming.MachinedSegments;

            _status.Text = "正在统计零件数量…";
            Application.DoEvents();

            var rows = PartListService.FromAssembly(swApp, assembly, quick, delegate { });
            _quantityValue.Text = PartListService.BuildSummary(rows);
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = Theme.Canvas
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 146f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 134f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildDocumentPanel(), 0, 1);
            root.Controls.Add(BuildPropertyPanel(), 0, 2);
            root.Controls.Add(BuildActionPanel(), 0, 3);
            root.Controls.Add(BuildStatusBar(), 0, 4);

            Controls.Add(root);
        }

        private Control BuildHeader()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Accent };
            var title = Theme.CreateLabel(AddinConstants.Title, Theme.Title, Color.White);
            title.Location = new Point(12, 10);

            var subtitle = Theme.CreateLabel("图号 · 材料 · 加工件数量", Theme.Small, Color.FromArgb(214, 232, 248));
            subtitle.Location = new Point(13, 32);

            panel.Controls.Add(title);
            panel.Controls.Add(subtitle);
            return panel;
        }

        private Control BuildDocumentPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(10, 8, 10, 8) };

            var caption = Theme.CreateLabel("当前文档", Theme.BodyBold, Theme.Text);
            caption.Dock = DockStyle.Top;
            caption.Height = 20;

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                BackColor = Theme.Surface
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (var i = 0; i < 5; i++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.Percent, 20f));
            }

            AddField(table, 0, "文件", _fileValue);
            AddField(table, 1, "类型", _typeValue);
            AddField(table, 2, "图号", _partNumberValue);
            AddField(table, 3, "材料", _materialValue);
            AddField(table, 4, "配置/数量", _quantityValue);

            panel.Controls.Add(table);
            panel.Controls.Add(caption);
            return panel;
        }

        private static void AddField(TableLayoutPanel table, int row, string label, Label value)
        {
            var caption = Theme.CreateFieldLabel(label);
            caption.Dock = DockStyle.Fill;
            value.Dock = DockStyle.Fill;
            value.Cursor = Cursors.Hand;

            table.Controls.Add(caption, 0, row);
            table.Controls.Add(value, 1, row);
        }

        private Control BuildPropertyPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(10, 6, 10, 8) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Theme.Surface
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));

            var caption = Theme.CreateLabel("自定义属性", Theme.BodyBold, Theme.Text);
            caption.Dock = DockStyle.Fill;
            layout.Controls.Add(caption, 0, 0);

            BuildGrid();
            layout.Controls.Add(_grid, 0, 1);

            var toolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = Theme.Surface
            };
            for (var i = 0; i < 4; i++)
            {
                toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            }

            foreach (var button in new[] { _refreshButton, _addButton, _removeButton, _applyButton })
            {
                button.Dock = DockStyle.Fill;
                button.Margin = new Padding(2, 3, 2, 3);
            }

            toolbar.Controls.Add(_refreshButton, 0, 0);
            toolbar.Controls.Add(_addButton, 1, 0);
            toolbar.Controls.Add(_removeButton, 2, 0);
            toolbar.Controls.Add(_applyButton, 3, 0);
            layout.Controls.Add(toolbar, 0, 2);

            panel.Controls.Add(layout);
            return panel;
        }

        private void BuildGrid()
        {
            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = true;
            _grid.AllowUserToDeleteRows = true;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            Theme.StyleGrid(_grid);

            var nameColumn = new DataGridViewTextBoxColumn
            {
                HeaderText = "属性名",
                Width = 104,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };

            var typeColumn = new DataGridViewComboBoxColumn
            {
                HeaderText = "类型",
                Width = 62,
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
        }

        private Control BuildActionPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas, Padding = new Padding(10, 4, 10, 4) };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Theme.Canvas
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var partListButton = Theme.CreatePrimaryButton("明细汇总（图号 / 材料 / 数量）…");
            partListButton.Dock = DockStyle.Fill;
            partListButton.Margin = new Padding(0, 2, 0, 2);
            partListButton.Click += delegate { _host.ShowPartListDialog(); };
            layout.Controls.Add(partListButton, 0, 0);

            var exportButton = Theme.CreateSecondaryButton("批量导出 PDF / DWG / STEP…");
            exportButton.Dock = DockStyle.Fill;
            exportButton.Margin = new Padding(0, 2, 0, 2);
            exportButton.Click += delegate { _host.ShowBatchExportDialog(); };
            layout.Controls.Add(exportButton, 0, 1);

            var propertyButton = Theme.CreateSecondaryButton("属性批量写入…");
            propertyButton.Dock = DockStyle.Fill;
            propertyButton.Margin = new Padding(0, 2, 0, 2);
            propertyButton.Click += delegate { _host.ShowPropertyToolDialog(); };
            layout.Controls.Add(propertyButton, 0, 2);

            layout.Controls.Add(BuildPrefixRow(), 0, 3);

            _syncDelete.Text = "应用时删除表中未列出的属性";
            _syncDelete.Font = Theme.Small;
            _syncDelete.ForeColor = Theme.Muted;
            _syncDelete.AutoSize = true;
            _syncDelete.Margin = new Padding(0, 4, 0, 0);
            layout.Controls.Add(_syncDelete, 0, 4);

            panel.Controls.Add(layout);
            return panel;
        }

        /// <summary>
        /// 标准件前缀快捷栏：把配置中的每个前缀直接显示成小按钮。
        /// 选中组件后点按钮即可改名，无需先从下拉框选择。
        /// </summary>
        private Control BuildPrefixRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Theme.Canvas
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var caption = Theme.CreateFieldLabel("前缀");
            caption.Dock = DockStyle.Fill;
            caption.TextAlign = ContentAlignment.MiddleLeft;

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Canvas,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            var prefixes = NamingOptionsFactory.ParsePrefixes(_host.Settings.BomPrefixes);
            foreach (var prefix in prefixes)
            {
                var value = prefix;
                var button = Theme.CreateSecondaryButton(value);
                button.AutoSize = false;
                button.Width = Math.Max(46, TextRenderer.MeasureText(value, Theme.Body).Width + 18);
                button.Height = 28;
                button.Margin = new Padding(0, 2, 4, 2);
                button.Click += delegate
                {
                    _host.ApplyPrefix(value, false);
                    RefreshDocument(false);
                };
                buttons.Controls.Add(button);
            }

            var remove = Theme.CreateSecondaryButton("去前缀");
            remove.AutoSize = false;
            remove.Width = 62;
            remove.Height = 28;
            remove.Margin = new Padding(4, 2, 0, 2);
            remove.Click += delegate
            {
                _host.ApplyPrefix(string.Empty, true);
                RefreshDocument(false);
            };
            buttons.Controls.Add(remove);

            row.Controls.Add(caption, 0, 0);
            row.Controls.Add(buttons, 1, 0);
            return row;
        }

        private Control BuildStatusBar()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(238, 241, 244) };
            _status.Dock = DockStyle.Fill;
            _status.Font = Theme.Small;
            _status.ForeColor = Theme.Muted;
            _status.Padding = new Padding(10, 0, 6, 0);
            _status.Cursor = Cursors.Hand;
            _status.Click += delegate { OpenLogFolder(); };
            panel.Controls.Add(_status);
            return panel;
        }

        private void WireEvents()
        {
            _refreshButton.Click += delegate { RefreshDocument(true); };
            _applyButton.Click += delegate { ApplyProperties(); };

            // 点击图号 / 材料直接复制，方便贴到图纸或 ERP 里
            _partNumberValue.Click += delegate { CopyToClipboard("图号", _partNumberValue.Text); };
            _materialValue.Click += delegate { CopyToClipboard("材料", _materialValue.Text); };
            _fileValue.Click += delegate { CopyToClipboard("文件名", _fileValue.Text); };

            _addButton.Click += delegate
            {
                if (_grid.Rows.Count > 0)
                {
                    _grid.CurrentCell = _grid.Rows[_grid.Rows.Count - 1].Cells[0];
                }

                _grid.Focus();
            };

            _removeButton.Click += delegate
            {
                var removed = 0;
                foreach (var row in new List<DataGridViewRow>(SelectedRows()))
                {
                    if (!row.IsNewRow)
                    {
                        _grid.Rows.Remove(row);
                        removed++;
                    }
                }

                if (removed == 0)
                {
                    _status.Text = "请先选中要删除的行";
                }
            };
        }

        private void CopyToClipboard(string label, string value)
        {
            if (string.IsNullOrEmpty(value) || value == "-")
            {
                return;
            }

            try
            {
                Clipboard.SetText(value);
                _status.Text = string.Format("已复制{0}：{1}", label, value);
            }
            catch (Exception ex)
            {
                Log.Warn("复制到剪贴板失败：" + ex.Message);
            }
        }

        private IEnumerable<DataGridViewRow> SelectedRows()
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Selected)
                {
                    yield return row;
                }
            }
        }

        private void ApplyProperties()
        {
            var doc = SwUtils.ActiveDoc(_host.SwApp);
            if (doc == null)
            {
                MessageBox.Show("请先打开一个零件、装配体或工程图。", AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var configuration = PropertyService.DocumentLevelConfiguration;
            var written = 0;
            var failed = 0;
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

                CustomProperty original;
                var changed = !_original.TryGetValue(name, out original)
                              || original.Type != type
                              || !string.Equals(original.ResolvedValue ?? string.Empty, value, StringComparison.Ordinal);

                if (!changed)
                {
                    continue;
                }

                if (PropertyService.Write(doc, configuration, new CustomProperty(name, type, value), true))
                {
                    written++;
                }
                else
                {
                    failed++;
                }
            }

            var removed = 0;
            if (_syncDelete.Checked)
            {
                foreach (var pair in _original)
                {
                    if (!seen.Contains(pair.Key) && PropertyService.Delete(doc, configuration, pair.Key))
                    {
                        removed++;
                    }
                }
            }

            Log.Info(string.Format("属性更新完成：写入 {0} 项，删除 {1} 项，失败 {2} 项。", written, removed, failed));
            RefreshDocument(false);
        }

        private static void OpenLogFolder()
        {
            try
            {
                AppPaths.Ensure(AppPaths.Logs);
                Process.Start("explorer.exe", AppPaths.Logs);
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开日志目录：" + ex.Message, AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnLogMessage(string message)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action<string>(OnLogMessage), message);
                    return;
                }

                _status.Text = message.Length > 90 ? message.Substring(0, 90) + "…" : message;
            }
            catch
            {
                // 面板可能正在销毁
            }
        }
    }
}
