using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MechKit.Core;

namespace MechKit.UI
{
    internal sealed class CommandPanel : UserControl
    {
        private readonly MechKitAddin _addin;
        private readonly FlowLayoutPanel _groups;
        private readonly List<Image> _images = new List<Image>();
        internal CommandPanel(MechKitAddin addin)
        {
            _addin = addin;
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            _groups = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true,
                FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8), BackColor = Theme.Canvas };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _groups.Margin = Padding.Empty;
            layout.Controls.Add(_groups, 0, 0);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty,
                Padding = new Padding(8), BackColor = Theme.Canvas, WrapContents = true };
            var footerLabels = new[] { "设置", "导出配置", "关于" };
            var footerActions = new Action[] { _addin.OnSettings, _addin.OnExportConfiguration, _addin.OnAbout };
            for (var i = 0; i < footerLabels.Length; i++)
            {
                var run = footerActions[i];
                var button = Theme.CreateSecondaryButton(footerLabels[i]);
                button.AutoSize = true;
                button.Click += delegate { run(); };
                footer.Controls.Add(button);
            }
            layout.Controls.Add(footer, 0, 1);
            Controls.Add(layout);
            Rebuild();
        }
        internal void Rebuild()
        {
            _groups.SuspendLayout();
            try
            {
                while (_groups.Controls.Count > 0) _groups.Controls[0].Dispose();
                foreach (var image in _images) image.Dispose();
                _images.Clear();
                AddGroup("常用工具", Color.White, new[] { "预览和导出 BOM", "属性批量写入", "大类管理", "打开所在文件夹" },
                    new Action[] { _addin.OnPartList, _addin.OnPropertyTool, _addin.OnExternalDrawingCommand, _addin.OnOpenDocumentFolder }, 10);
                AddGroup("工程图", Color.FromArgb(238,230,250), new[] { "板件一键出图", "车铣件出图", "标注助手", "激光切割 DXF", "一键拷贝 p6061" },
                    new Action[] { _addin.OnPlateDrawing, _addin.OnMachiningDrawing, _addin.OnQuickAnnotation, _addin.OnLaserDxf, _addin.OnCopyP6061 }, 7);
                AddGroup("命名工具", Theme.Canvas, new[] { "下划线转短横线", "撤回命名" },
                    new Action[] { _addin.OnConvertUnderscoresCommand, _addin.OnMachinedUndoCommand }, 10);
                AddGroup("日期 / 装配 / 组件", Color.FromArgb(238,230,250),
                    new[] { "写入当天日期", "写入装配", "写入组件", "另存虚拟装配体" },
                    new Action[] { _addin.OnMachinedDateCommand, _addin.OnAssemblyNameCommand, _addin.OnComponentNameCommand, _addin.OnSaveVirtualAssembly }, 10);
                var labels = new List<string> { "命名规则" };
                var actions = new List<Action> { _addin.OnPartNamingRule };
                Append(labels, actions, _addin.BuildMachinedLevel2ButtonList(), "", _addin.OnMachinedLevel2Command);
                Append(labels, actions, _addin.BuildMachinedLevel3ButtonList(), "名称：", _addin.OnMachinedLevel3Command);
                AddGroup("加工件", Color.FromArgb(232,246,233), labels, actions, 10, 1, separatorRows: new[] { 0, 1 });
                labels = new List<string> { "前缀设置" };
                actions = new List<Action> { _addin.OnStandardPrefix };
                Append(labels, actions, _addin.BuildPrefixButtonList(), "", _addin.OnPrefixCommand);
                Append(labels, actions, _addin.BuildMiddleNameButtonList(), "", _addin.OnMiddleNameCommand);
                AddGroup("标准件", Color.FromArgb(231,242,255), labels, actions, 10, 1, _addin.BuildPrefixButtonList().Count, new[] { 1, 2 });
                foreach(var category in PartCategory.Load(_addin.Settings))
                {
                    var prefixes=NamingOptionsFactory.ParsePrefixes(category.Prefixes,true);
                    var categoryLabels=new List<string>();var categoryActions=new List<Action>();
                    foreach(var prefix in prefixes) {var value=prefix;categoryLabels.Add(value);categoryActions.Add(()=>_addin.ApplyPrefix(value,false));}
                    var color=category.ExcludeFromBom ? Color.White : category.Name=="库存件" ? Color.FromArgb(224,245,242) : Color.FromArgb(255,247,210);
                    AddGroup(category.Name,color,categoryLabels,categoryActions,10);
                }
            }
            finally { _groups.ResumeLayout(true); }
        }
        private static void Append(List<string> labels, List<Action> actions, IList<string> values, string prefix, Action<string> execute)
        {
            for (var i = 0; i < values.Count; i++)
            {
                var index = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                labels.Add(prefix + values[i]);
                actions.Add(() => execute(index));
            }
        }
        private void AddGroup(string title, Color color, IList<string> labels, IList<Action> actions, int icon, int headerCount = 0, int prefixCount = 0, int[] separatorRows = null)
        {
            var section = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Size = Size.Empty, ColumnCount = 1, RowCount = 2, BackColor = color, Padding = new Padding(5), Margin = new Padding(0,0,0,5) };
            if (!string.IsNullOrEmpty(title)) section.Controls.Add(new Label { Text = title, AutoSize = true, Font = Theme.BodyBold, Margin = new Padding(3,0,3,4) }, 0, 0);
            var bodyCount = labels.Count - headerCount - prefixCount;
            var common = icon == 0;
            var grid = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Size = Size.Empty, ColumnCount = common ? 3 : Math.Max(Math.Max(headerCount, prefixCount), (bodyCount + 3) / 4),
                RowCount = common ? (labels.Count + 2) / 3 : (headerCount > 0 ? 1 : 0) + (prefixCount > 0 ? 1 : 0) + Math.Min(4, bodyCount), Margin = Padding.Empty };
            for (var c = 0; c < grid.ColumnCount; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (var r = 0; r < grid.RowCount; r++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (separatorRows != null)
            {
                grid.Paint += (sender, e) =>
                {
                    var heights = grid.GetRowHeights();
                    using (var pen = new Pen(Color.FromArgb(210, 214, 218)))
                    {
                        foreach (var boundary in separatorRows)
                        {
                            if (boundary > heights.Length) continue;
                            var y = 0;
                            for (var row = 0; row < boundary; row++) y += heights[row];
                            e.Graphics.DrawLine(pen, 0, Math.Max(0, y - 1), grid.ClientSize.Width - 1, Math.Max(0, y - 1));
                        }
                    }
                };
                grid.SizeChanged += (sender, e) => grid.Invalidate();
            }
            for (var i = 0; i < labels.Count; i++)
            {
                var run = actions[i];
                var button = new Button { Text = labels[i], AutoSize = true, MinimumSize = new Size(86,28),
                    Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = color, Font = Theme.Body,
                    TextAlign = ContentAlignment.MiddleLeft, ImageAlign = ContentAlignment.MiddleLeft,
                    TextImageRelation = TextImageRelation.ImageBeforeText, Padding = new Padding(2,1,2,1), Margin = new Padding(1), UseVisualStyleBackColor = false };
                button.FlatAppearance.BorderSize = 0;
                if (IconResources.SmallIcons.Length > 0)
                {
                    using (var strip = new Bitmap(IconResources.SmallIcons[0]))
                    {
                        var size = strip.Height;
                        var iconIndex = icon == 0 ? new[] { 1, 4, 7, 8 }[i] : icon;
                        if ((iconIndex + 1) * size <= strip.Width)
                        {
                            button.Image = strip.Clone(new Rectangle(iconIndex * size, 0, size, size), strip.PixelFormat);
                            _images.Add(button.Image);
                        }
                    }
                }
                button.Click += delegate { run(); };
                if (common) grid.Controls.Add(button, i % 3, i / 3);
                else if (i < headerCount) grid.Controls.Add(button, i, 0);
                else if (i < headerCount + prefixCount) grid.Controls.Add(button, i - headerCount, headerCount > 0 ? 1 : 0);
                else
                {
                    var bodyIndex = i - headerCount - prefixCount;
                    grid.Controls.Add(button, bodyIndex / 4, bodyIndex % 4 + (headerCount > 0 ? 1 : 0) + (prefixCount > 0 ? 1 : 0));
                }
            }
            section.Controls.Add(grid, 0, 1);
            _groups.Controls.Add(section);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (var image in _images) image.Dispose();
            base.Dispose(disposing);
        }
    }
}
