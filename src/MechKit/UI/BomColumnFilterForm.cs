using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MechKit.UI
{
    internal sealed class BomColumnFilterForm : Form
    {
        private readonly CheckedListBox _values = new CheckedListBox { Dock=DockStyle.Fill, CheckOnClick=true };
        private readonly List<string> _all;
        internal HashSet<string> SelectedValues { get; private set; }
        internal BomColumnFilterForm(string title, IEnumerable<string> values, HashSet<string> selected)
        {
            Text="筛选："+title;Font=Theme.Body;ShowInTaskbar=false;
            FormBorderStyle=FormBorderStyle.FixedToolWindow;StartPosition=FormStartPosition.Manual;
            ClientSize=new Size(310,380);MaximizeBox=false;MinimizeBox=false;
            _all=new List<string>(new SortedSet<string>(values,StringComparer.OrdinalIgnoreCase));
            var layout=new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(8),ColumnCount=1,RowCount=3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
            var options=new FlowLayoutPanel { Dock=DockStyle.Fill };
            foreach(var text in new[]{"全选","全不选","清除筛选"})
            {
                var button=Theme.CreateSecondaryButton(text);button.AutoSize=true;
                var action=text;
                button.Click+=delegate
                {
                    if(action=="清除筛选"){SelectedValues=null;DialogResult=DialogResult.OK;Close();return;}
                    for(int i=0;i<_values.Items.Count;i++)_values.SetItemChecked(i,action=="全选");
                };
                options.Controls.Add(button);
            }
            for(int i=0;i<_all.Count;i++)_values.Items.Add(_all[i].Length==0?"（空白）":_all[i],selected==null || selected.Contains(_all[i]));
            var footer=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft };
            var cancel=Theme.CreateSecondaryButton("取消");cancel.DialogResult=DialogResult.Cancel;
            var ok=Theme.CreatePrimaryButton("确定");
            ok.Click+=delegate
            {
                SelectedValues=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(int i in _values.CheckedIndices)SelectedValues.Add(_all[i]);
                if(SelectedValues.Count==_all.Count)SelectedValues=null;
                DialogResult=DialogResult.OK;Close();
            };
            footer.Controls.Add(cancel);footer.Controls.Add(ok);AcceptButton=ok;CancelButton=cancel;
            layout.Controls.Add(options,0,0);layout.Controls.Add(_values,0,1);layout.Controls.Add(footer,0,2);Controls.Add(layout);
        }
    }
}
