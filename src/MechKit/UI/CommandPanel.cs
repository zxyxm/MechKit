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
            Controls.Add(_groups);
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
                AddGroup("常用工具", Color.White, new[] { "生成 BOM", "明细汇总", "批量导出", "板件一键出图", "公差助手", "导出配置", "设置", "关于" },
                    new Action[] { _addin.OnGenerateBom, _addin.OnPartList, _addin.OnBatchExport, _addin.OnPlateDrawing, _addin.OnQuickAnnotation, _addin.OnExportConfiguration, _addin.OnSettings, _addin.OnAbout }, 0);
                AddGroup("", Theme.Canvas, new[] { "下划线转短横线" }, new Action[] { _addin.OnConvertUnderscoresCommand }, 10);
                var labels = new List<string> { "命名规则", "写入当天日期" };
                var actions = new List<Action> { _addin.OnPartNamingRule, _addin.OnMachinedDateCommand };
                Append(labels, actions, _addin.BuildMachinedLevel2ButtonList(), "", _addin.OnMachinedLevel2Command);
                Append(labels, actions, _addin.BuildMachinedLevel3ButtonList(), "名称：", _addin.OnMachinedLevel3Command);
                AddGroup("加工件", Color.FromArgb(232,246,233), labels, actions, 10, 2);
                labels = new List<string> { "前缀设置" };
                actions = new List<Action> { _addin.OnStandardPrefix };
                Append(labels, actions, _addin.BuildPrefixButtonList(), "", _addin.OnPrefixCommand);
                Append(labels, actions, _addin.BuildMiddleNameButtonList(), "", _addin.OnMiddleNameCommand);
                AddGroup("标准件", Color.FromArgb(231,242,255), labels, actions, 10, 1, _addin.BuildPrefixButtonList().Count);
                AddGroup("装配 / 组件", Color.FromArgb(238,230,250), new[] { "添加装配前缀" }, new Action[] { _addin.OnAssemblyNameCommand }, 10);
                AddGroup("库存件", Color.FromArgb(224,245,242), new[] { "添加库存前缀" }, new Action[] { _addin.OnStockPartCommand }, 10);
                AddGroup("备件", Color.FromArgb(240,229,250), new[] { "添加备件前缀" }, new Action[] { _addin.OnSparePartCommand }, 10);
                AddGroup("外部图纸", Color.FromArgb(255,247,210), new[] { "图纸前缀设置" }, new Action[] { _addin.OnExternalDrawingCommand }, 10);
                AddGroup("参考件", Color.White, new[] { "添加参考前缀" }, new Action[] { _addin.OnReferencePartCommand }, 10);
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
        private void AddGroup(string title, Color color, IList<string> labels, IList<Action> actions, int icon, int headerCount = 0, int prefixCount = 0)
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
                        var iconIndex = icon == 0 ? new[] { 0, 1, 4, 4, 7, 4, 7, 8 }[i] : icon;
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
