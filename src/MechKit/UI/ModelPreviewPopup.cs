using System;
using System.Drawing;
using System.Windows.Forms;

namespace MechKit.UI
{
    // Unlike ToolTip, this popup has no auto-hide timeout and does not take focus.
    internal sealed class ModelPreviewPopup : Form
    {
        private string _name, _location;
        private Image _image;
        private int _locationHeight;
        public ModelPreviewPopup()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.White;
            DoubleBuffered = true;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var value=base.CreateParams; value.ExStyle |= 0x08000000 | 0x00000080; return value; }
        }
        internal void SetPreview(string name, string location, Image image)
        {
            _name=name ?? ""; _location="完整位置："+(location ?? ""); _image=image;
            _locationHeight=TextRenderer.MeasureText(_location,Theme.Body,new Size(496,int.MaxValue),
                TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height;
            ClientSize=new Size(520,380+Math.Max(34,_locationHeight)+24);
            Invalidate();
        }
        internal void ClearPreview() { Hide(); _image=null; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using(var pen=new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen,0,0,ClientSize.Width-1,ClientSize.Height-1);
            TextRenderer.DrawText(e.Graphics,_name,Theme.BodyBold,new Rectangle(12,10,496,30),Theme.Text,
                TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            var area=new Rectangle(12,48,496,320);
            if(_image==null) TextRenderer.DrawText(e.Graphics,"暂无已保存的缩略图",Theme.Body,area,Theme.Muted,
                TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            else
            {
                var scale=Math.Min((float)area.Width/_image.Width,(float)area.Height/_image.Height);
                var w=_image.Width*scale;var h=_image.Height*scale;
                e.Graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(_image,area.Left+(area.Width-w)/2,area.Top+(area.Height-h)/2,w,h);
            }
            using(var pen=new Pen(Theme.Border)) e.Graphics.DrawLine(pen,12,378,508,378);
            TextRenderer.DrawText(e.Graphics,_location,Theme.Body,new Rectangle(12,388,496,_locationHeight),Theme.Text,
                TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
        }
    }
}
