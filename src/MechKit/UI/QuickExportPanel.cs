using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MechKit.Core;
using MechKit.Features;

namespace MechKit.UI
{
    internal sealed class QuickExportPanel : FlowLayoutPanel
    {
        internal QuickExportPanel(MechKitAddin addin)
        {
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Color.FromArgb(232, 246, 233);
            Padding = new Padding(5);
            Margin = new Padding(0, 0, 0, 5);
            Controls.Add(new Label { Text = "快捷导出 · 整个当前装配体", AutoSize = true, Font = Theme.BodyBold });
            var folder = new TextBox { ReadOnly = true, Width = 235, Text = QuickExport.Folder };
            var browse = Theme.CreateSecondaryButton("1. 选择输出文件夹…");
            browse.Width = 235;
            browse.Click += delegate { addin.OnQuickExportFolder(); folder.Text = QuickExport.Folder; };
            Controls.Add(browse);
            Controls.Add(folder);
            Controls.Add(new Label { Text = "2. 筛选大类", AutoSize = true });
            var category = new ComboBox { Width = 235, DropDownStyle = ComboBoxStyle.DropDownList };
            category.Items.AddRange(new object[] { "全部", "加工件", "标准件" });
            foreach (var item in PartCategory.Load(addin.Settings))
                if (!category.Items.Contains(item.Name)) category.Items.Add(item.Name);
            category.Items.Add("未分类");
            category.SelectedItem = category.Items.Contains(QuickExport.CategoryFilter) ? QuickExport.CategoryFilter : "全部";
            Controls.Add(category);
            var secondaryLabel = new Label { AutoSize = true };
            Controls.Add(secondaryLabel);
            var filter = new ComboBox { Width = 235, DropDownStyle = ComboBoxStyle.DropDown, Text = QuickExport.FieldFilter };
            filter.Items.Add("全部");
            filter.TextChanged += delegate { QuickExport.FieldFilter = filter.Text; };
            Action refreshSecondary = delegate
            {
                var previous = filter.Text;
                try
                {
                    var doc = SwUtils.ActiveDoc(addin.SwApp);
                    var values = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    var naming = NamingOptionsFactory.FromSettings(addin.Settings);
                    if (doc != null)
                        foreach (var path in QuickExport.CollectAllParts(doc, Log.Info))
                        {
                            if (QuickExport.CategoryField(path, naming) != QuickExport.CategoryFilter) continue;
                            var field = QuickExport.SecondaryField(path, naming);
                            if (field.Length > 0) values.Add(field);
                        }
                    filter.Items.Clear();
                    filter.Items.Add("全部");
                    foreach (var value in values) filter.Items.Add(value);
                }
                catch (Exception ex) { Log.Warn("读取二级筛选字段失败：" + ex.Message); }
                finally { filter.Text = previous; }
            };
            filter.DropDown += delegate { refreshSecondary(); };
            Action updateCategory = delegate
            {
                QuickExport.CategoryFilter = category.Text;
                secondaryLabel.Text = category.Text == "加工件" ? "二级：工艺（CNC、钣金等）"
                    : category.Text == "标准件" ? "二级：中间名（五金、电机等）" : "二级：分类前缀";
                filter.Enabled = category.Text != "全部";
            };
            category.SelectedIndexChanged += delegate
            {
                filter.Text = "全部";
                filter.Items.Clear();
                filter.Items.Add("全部");
                updateCategory();
            };
            updateCategory();
            Controls.Add(filter);
            var paired = new CheckBox { Text = "STP/PDF：仅有工程图的零件", AutoSize = true, Checked = QuickExport.OnlyWithDrawing };
            paired.CheckedChanged += delegate { QuickExport.OnlyWithDrawing = paired.Checked; };
            Controls.Add(paired);
            var overwrite = new CheckBox { Text = "覆盖已有 STP/PDF", AutoSize = true, Checked = QuickExport.Overwrite };
            overwrite.CheckedChanged += delegate { QuickExport.Overwrite = overwrite.Checked; };
            Controls.Add(overwrite);
            var bom = Theme.CreateSecondaryButton("3. 导出 BOM 表（Excel）");
            var files = Theme.CreatePrimaryButton("导出 STP＋PDF");
            bom.Width = files.Width = 235;
            bom.Click += delegate { addin.OnQuickExportBom(); };
            files.Click += delegate { addin.OnQuickExport(); };
            Controls.Add(bom);
            Controls.Add(files);
            Controls.Add(new Label { Text = "先选大类，再选二级条件。\r\n二级条件可用逗号分隔；全部表示不筛选。\r\n两个按钮共用目录和两级筛选。\r\nBOM 保留已有分类收录规则。\r\n零件导出 STP，同名工程图导出 PDF。\r\n压缩组件不导出；重复零件只导出一次。", AutoSize = true, MaximumSize = new Size(235, 0) });
        }
    }
}
