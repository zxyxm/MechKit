using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MechKit.Core;
using MechKit.Features;
using SolidWorks.Interop.sldworks;

namespace MechKit.UI
{
    internal sealed class PlateDrawingForm : Form
    {
        public static bool Running { get; private set; }
        private bool cancel;
        private bool finished;
        private readonly Label status=new Label{Dock=DockStyle.Top,Height=58,Padding=new Padding(12)};
        private readonly ListView rows=new ListView{Dock=DockStyle.Fill,View=System.Windows.Forms.View.Details,FullRowSelect=true};
        private readonly Button stop=new Button{Text="停止",AutoSize=true};
        private readonly Button report=new Button{Text="查看处理记录",AutoSize=true,Enabled=false};
        private readonly ISldWorks app;
        private readonly AddinSettings settings;
        private readonly ModelDoc2 source;
        public PlateDrawingForm(ISldWorks app,AddinSettings settings)
        {
            this.app=app; this.settings=settings; source=app.IActiveDoc2;
            Text="MechKit · 板件一键出图"; Size=new Size(980,520); MinimumSize=new Size(700,360);
            StartPosition=FormStartPosition.CenterParent;
            rows.Columns.Add("状态",130); rows.Columns.Add("零件 / 配置",260); rows.Columns.Add("处理结果",540);
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=45,Padding=new Padding(8),FlowDirection=FlowDirection.RightToLeft};
            buttons.Controls.Add(stop); buttons.Controls.Add(report);
            Controls.Add(rows); Controls.Add(status); Controls.Add(buttons);
            status.Text=(source.GetType()==SwUtils.DocPart
                ? "为当前零件的活动配置出图，已有同名工程图保留。"
                : "识别当前装配体中的非钣金板件。相同零件去重，已有同名工程图保留。")+
                "\r\n仅生成非钣金板件的同目录、同名 SLDDRW；不限定 6061 材料。";
            stop.Click+=(s,e)=>{if(finished) Close(); else {cancel=true;stop.Enabled=false;status.Text="正在停止，当前零件完成后结束…";}};
            FormClosing+=(s,e)=>{if(Running && !finished){cancel=true;e.Cancel=true;}};
            Shown+=(s,e)=>BeginInvoke(new Action(Execute));
            rows.DoubleClick+=(s,e)=>
            {
                if(!finished || rows.SelectedItems.Count==0) return;
                var r=rows.SelectedItems[0].Tag as PlateDrawingResult;
                if(r==null || string.IsNullOrEmpty(r.Output)) return;
                int errors=0,warnings=0;
                app.OpenDoc6(r.Output,3,1,"",ref errors,ref warnings);
            };
        }
        private void Execute()
        {
            if(Running) {Close();return;}
            Running=true;
            var service=new PlateDrawingService(app,settings);
            try
            {
                service.Run(source,(index,total,name)=>
                {
                    status.Text=string.Format("正在处理 {0}/{1}：{2}\r\n重复实例只出一份；有疑问的识别和标注会列入待复核。",index+1,total,name);
                    Refresh(); Application.DoEvents(); return !cancel;
                },r=>
                {
                    var item=new ListViewItem(new[]{r.Status,System.IO.Path.GetFileName(r.Source)+" ["+r.Configuration+"]",r.Message}){Tag=r};
                    rows.Items.Add(item); item.EnsureVisible();
                });
                status.Text=string.Format("{0}：生成 {1}，其中待复核 {2}；跳过 {3}，失败 {4}。完成后停留在最后生成的图纸；双击条目可切换图纸。",
                    service.Cancelled?"已停止":"处理完成",service.Results.Count(r=>r.Output!=""),
                    service.Results.Count(r=>r.Status=="已生成·待复核"),service.Results.Count(r=>r.Status=="跳过"),service.Results.Count(r=>r.Status=="失败"));
            }
            catch(Exception ex) {status.Text="出图中断："+ex.Message;Log.Error("板件出图中断",ex);}
            finally
            {
                finished=true; Running=false; stop.Text="关闭";stop.Enabled=true;
                report.Enabled=!string.IsNullOrEmpty(service.ReportPath);
                report.Click+=(s,e)=>{try{Process.Start(service.ReportPath);}catch(Exception ex){MessageBox.Show(ex.Message);}};
            }
        }
    }
}
