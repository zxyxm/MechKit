using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MechKit.Core;
namespace MechKit.UI
{
    internal sealed class CategoryRulesPanel : UserControl
    {
        private readonly IAddinHost _host;
        private readonly DataGridView _grid = new DataGridView();
        public CategoryRulesPanel(IAddinHost host, int category = -1)
        {
            _host=host; Dock=DockStyle.Fill; BackColor=Theme.Surface; Padding=new Padding(12);
            var layout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=4 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,64));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
            layout.Controls.Add(new Label { Dock=DockStyle.Fill,Text="固定大类：加工件、标准件。以下大类可以新增、修改或删除。",Font=Theme.BodyBold },0,0);
            _grid.Dock=DockStyle.Fill; _grid.AllowUserToAddRows=false; _grid.AllowUserToDeleteRows=false;
            _grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill; _grid.RowHeadersVisible=false;
            _grid.BackgroundColor=Color.White; _grid.RowTemplate.Height=32;
            _grid.Columns.Add(new DataGridViewTextBoxColumn {Name="name",HeaderText="大类名称",FillWeight=25});
            _grid.Columns.Add(new DataGridViewTextBoxColumn {Name="prefix",HeaderText="名称前缀（多条用分号分隔）",FillWeight=50});
            _grid.Columns.Add(new DataGridViewCheckBoxColumn {Name="exclude",HeaderText="不计入 BOM",FillWeight=17});
            _grid.Columns.Add(new DataGridViewButtonColumn {Name="delete",HeaderText="",Text="删除",UseColumnTextForButtonValue=true,FillWeight=12});
            _grid.CellContentClick+=(s,e)=> {if(e.RowIndex>=0 && e.ColumnIndex==3) _grid.Rows.RemoveAt(e.RowIndex);};
            layout.Controls.Add(_grid,0,1);
            layout.Controls.Add(new Label {Dock=DockStyle.Fill,Font=Theme.Small,Text="按名称开头匹配，最长前缀优先；例如 BRC- 只匹配 BRC- 开头。\r\n新增大类默认整体计入 BOM，子装配不展开；勾选后不计入 BOM。删除大类不会删除零件文件。"},0,2);
            var actions=new FlowLayoutPanel {Dock=DockStyle.Fill};
            var add=Theme.CreateSecondaryButton("新增大类"); add.Click+=(s,e)=>{var i=_grid.Rows.Add("","",false);_grid.CurrentCell=_grid.Rows[i].Cells[0];_grid.BeginEdit(true);};
            var save=Theme.CreatePrimaryButton("保存大类规则"); save.Width=148; save.Click+=(s,e)=>{try {SaveToSettings();_host.Settings.Save();_host.RefreshNamingCommands();save.Text="已保存";}catch(Exception ex){MessageBox.Show(ex.Message,"大类规则");}};
            actions.Controls.Add(add);actions.Controls.Add(save);layout.Controls.Add(actions,0,3);
            Controls.Add(layout);LoadFromSettings();
        }
        public void LoadFromSettings() { _grid.Rows.Clear();foreach(var c in PartCategory.Load(_host.Settings)) _grid.Rows.Add(c.Name,c.Prefixes,c.ExcludeFromBom); }
        public void ResetDefaults() { _grid.Rows.Clear();_grid.Rows.Add("参考件","参考",true);_grid.Rows.Add("库存件","库存",false);_grid.Rows.Add("外部图纸","外部图纸",false); }
        public void SaveToSettings()
        {
            _grid.EndEdit();var list=new List<PartCategory>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"加工件","标准件","装配","组件"};var prefixes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(DataGridViewRow row in _grid.Rows)
            {
                var name=Convert.ToString(row.Cells[0].Value).Trim();var prefix=Convert.ToString(row.Cells[1].Value).Trim();
                if(name.Length==0 || !names.Add(name)) throw new InvalidOperationException("大类名称不能为空、重复，或使用固定大类名称。");
                var parts=NamingOptionsFactory.ParsePrefixes(prefix,true);
                if(parts.Length==0) throw new InvalidOperationException(name+"：请填写至少一个前缀。");
                foreach(var p in parts) {if(p.StartsWith("淘宝",StringComparison.OrdinalIgnoreCase)||p.StartsWith("代理",StringComparison.OrdinalIgnoreCase)||NamingOptions.StartsWithDate(p)) throw new InvalidOperationException("前缀不能覆盖加工件或标准件规则："+p);if(!prefixes.Add(p)) throw new InvalidOperationException("前缀重复："+p);}
                list.Add(new PartCategory{Name=name,Prefixes=NamingOptionsFactory.SerializePrefixes(parts,true),ExcludeFromBom=Convert.ToBoolean(row.Cells[2].Value)});
            }
            PartCategory.Save(_host.Settings,list);
        }
    }
}
