using System;
using System.Drawing;
using System.Windows.Forms;
using MechKit.Features;

namespace MechKit.UI
{
    internal sealed class QuickAnnotationForm : Form
    {
        private readonly IAddinHost _host;
        private readonly Label _status;

        public QuickAnnotationForm(IAddinHost host)
        {
            _host = host;
            Text = "快捷标注 - MechKit";
            Font = Theme.Body;
            BackColor = Theme.Canvas;
            WindowLayout.Attach(this, host.Settings, new Size(500, 280), new Size(460, 250));
            var layout = new TableLayoutPanel
            { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(14) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            for (var i = 0; i < 3; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var hint = Theme.CreateValueLabel("先在工程图中选择尺寸，再点击预设。可多选尺寸。\r\n公差单位为 mm，使用双边公差显示上下偏差。");
            hint.Dock = DockStyle.Fill;
            layout.Controls.Add(hint, 0, 0);
            for (var i = 0; i < QuickAnnotationService.Presets.Length; i++)
            {
                var preset = QuickAnnotationService.Presets[i];
                var button = Theme.CreatePrimaryButton(preset.Name);
                button.Dock = DockStyle.Fill;
                button.Click += delegate
                {
                    try { _status.Text = QuickAnnotationService.Apply(_host.SwApp, preset); }
                    catch (Exception ex) { _status.Text = ex.Message; }
                };
                layout.Controls.Add(button, 0, i + 1);
            }
            _status = Theme.CreateValueLabel("等待选择尺寸。销钉孔预设用于直径尺寸。");
            _status.Dock = DockStyle.Fill;
            layout.Controls.Add(_status, 0, 4);
            Controls.Add(layout);
        }
    }
}
