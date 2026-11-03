using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MechKit.Core;
using MechKit.Features;

namespace MechKit.UI
{
    internal sealed class TechnicalRequirementsForm : Form
    {
        private readonly IAddinHost _host;
        private readonly RequirementLibrary _library;
        private readonly CheckedListBox _items = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, HorizontalScrollbar = true };
        private readonly TextBox _editor = new TextBox { Width = 420 };
        private readonly TextBox _preview = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
        private readonly ComboBox _material = new ComboBox { Width = 150 };
        private readonly ComboBox _recipe = new ComboBox { Width = 150 };
        private readonly Label _status = new Label { AutoSize = true };
        private readonly NumericUpDown _x = new NumericUpDown { Minimum = 1, Maximum = 5000, Value = 20, Width = 65 };
        private readonly NumericUpDown _y = new NumericUpDown { Minimum = 1, Maximum = 5000, Value = 70, Width = 65 };
        private readonly CheckBox _replace = new CheckBox { Text = "替换图中已选注释（保留原位置）", AutoSize = true };

        internal TechnicalRequirementsForm(IAddinHost host)
        {
            _host = host;
            _library = RequirementLibrary.Load(RequirementLibrary.DefaultPath);
            Text = "技术要求 - MechKit";
            Font = Theme.Body;
            WindowLayout.Attach(this, host.Settings, new Size(850, 650), new Size(720, 540), false);
            TopMost = true; MinimizeBox = false; ShowInTaskbar = false;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(10) };
            for (var i = 0; i < 8; i++) layout.RowStyles.Add(new RowStyle(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
            layout.Controls.Add(new Label { Text = "公共条目 + 材料条目可组合；勾选后插入。保存配方可重复调用。", AutoSize = true }, 0, 0);
            var materialBar = Bar();
            materialBar.Controls.Add(new Label { Text = "材料", AutoSize = true }); materialBar.Controls.Add(_material);
            var naming = NamingOptionsFactory.FromSettings(host.Settings);
            foreach (var material in naming.MachinedMaterialProcessPresets.Values.Select(p => p.Material).Concat(_library.Groups.Keys.Where(k => k.StartsWith("材料:")).Select(k => k.Substring(3))).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s)) _material.Items.Add(material);
            Button(materialBar, "识别当前材料", () => { _material.Text = TechnicalRequirements.DetectMaterial(host.SwApp, NamingOptionsFactory.FromSettings(host.Settings)); _status.Text = "请核对材料；可手动输入或选择。"; });
            Button(materialBar, "加载公共＋材料", () => Fill(_library.Combined(_material.Text), true));
            Button(materialBar, "仅加载公共", () => Fill(_library.Get("公共"), true));
            layout.Controls.Add(materialBar, 0, 1);
            var recipes = Bar(); recipes.Controls.Add(new Label { Text = "配方名", AutoSize = true }); recipes.Controls.Add(_recipe);
            RefreshRecipes();
            Button(recipes, "加载配方", () => { var key = RecipeKey(); if (!_library.Groups.ContainsKey(key)) throw new InvalidOperationException("未找到该配方。"); Fill(_library.Get(key), true); });
            Button(recipes, "保存勾选为配方", () => SaveGroup(RecipeKey()));
            layout.Controls.Add(recipes, 0, 2);
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.Controls.Add(new Label { Text = "勾选条目（按列表顺序编号）", AutoSize = true }, 0, 0);
            body.Controls.Add(new Label { Text = "插入预览", AutoSize = true }, 1, 0);
            body.Controls.Add(_items, 0, 1); body.Controls.Add(_preview, 1, 1); layout.Controls.Add(body, 0, 3);
            _items.ItemCheck += delegate { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(Preview)); };
            _items.SelectedIndexChanged += delegate { if (_items.SelectedItem != null) _editor.Text = _items.SelectedItem.ToString(); };
            var edits = Bar(); edits.Controls.Add(_editor);
            Button(edits, "新增", () => { var text = ItemText(); if (!_items.Items.Contains(text)) _items.Items.Add(text, true); Preview(); });
            Button(edits, "修改选中行", () => { if (_items.SelectedIndex < 0) throw new InvalidOperationException("请先选中一行。"); _items.Items[_items.SelectedIndex] = ItemText(); Preview(); });
            Button(edits, "移除选中行", () => { if (_items.SelectedIndex >= 0) _items.Items.RemoveAt(_items.SelectedIndex); Preview(); });
            layout.Controls.Add(edits, 0, 4);
            var libraryBar = Bar();
            Button(libraryBar, "上移", () => MoveItem(-1)); Button(libraryBar, "下移", () => MoveItem(1));
            Button(libraryBar, "保存勾选为公共", () => SaveGroup("公共"));
            Button(libraryBar, "保存勾选为该材料", () => { if (string.IsNullOrWhiteSpace(_material.Text)) throw new InvalidOperationException("请填写材料。"); SaveGroup("材料:" + _material.Text.Trim()); });
            Button(libraryBar, "仅加载该材料", () => Fill(_library.Get("材料:" + _material.Text.Trim()), true));
            layout.Controls.Add(libraryBar, 0, 5);
            var insertBar = Bar(); insertBar.Controls.Add(new Label { Text = "距图纸左下角 X / Y（mm）", AutoSize = true }); insertBar.Controls.Add(_x); insertBar.Controls.Add(_y); insertBar.Controls.Add(_replace);
            Button(insertBar, "插入 / 替换技术要求", () => { TechnicalRequirements.Insert(_host.SwApp, RequirementLibrary.Compose(Selected()), _replace.Checked, (double)_x.Value, (double)_y.Value); _status.Text = "技术要求已写入当前图纸，可拖动调整位置；尚未自动保存工程图。"; });
            layout.Controls.Add(insertBar, 0, 6); layout.Controls.Add(_status, 0, 7);
            Controls.Add(layout);
            Shown += delegate
            {
                try { _material.Text = TechnicalRequirements.DetectMaterial(host.SwApp, naming); } catch (Exception ex) { _status.Text = ex.Message; }
                Fill(_library.Combined(_material.Text), false);
            };
        }
        private static FlowLayoutPanel Bar() { return new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true }; }
        private void Button(Control bar, string text, Action action)
        {
            var button = Theme.CreateSecondaryButton(text); button.AutoSize = true;
            button.Click += delegate { try { action(); } catch (Exception ex) { _status.Text = ex.Message; Log.Error("技术要求操作失败", ex); } };
            bar.Controls.Add(button);
        }
        private string ItemText() { var text = _editor.Text.Trim(); if (text.Length == 0) throw new InvalidOperationException("请填写条目内容。"); return text; }
        private string RecipeKey() { if (string.IsNullOrWhiteSpace(_recipe.Text)) throw new InvalidOperationException("请填写配方名。"); return "配方:" + _recipe.Text.Trim(); }
        private List<string> Selected() { return _items.CheckedItems.Cast<object>().Select(o => o.ToString()).ToList(); }
        private void Preview() { if (!IsDisposed) _preview.Text = RequirementLibrary.Compose(Selected()); }
        private void Fill(IEnumerable<string> lines, bool check)
        {
            _items.Items.Clear(); foreach (var line in RequirementLibrary.Normalize(lines)) _items.Items.Add(line, check); Preview();
            _status.Text = "共 " + _items.Items.Count + " 条；保存公共或材料库会以当前勾选内容替换该组。";
        }
        private void SaveGroup(string key)
        {
            var selected = Selected();
            if (selected.Count == 0) throw new InvalidOperationException("请至少勾选一条再保存。");
            _library.Groups[key] = selected; _library.Save(RequirementLibrary.DefaultPath); RefreshRecipes();
            _status.Text = "已保存 " + key + "（" + selected.Count + " 条）。";
        }
        private void RefreshRecipes() { var text = _recipe.Text; _recipe.Items.Clear(); foreach (var key in _library.Groups.Keys.Where(k => k.StartsWith("配方:")).OrderBy(k => k)) _recipe.Items.Add(key.Substring(3)); _recipe.Text = text; }
        private void MoveItem(int delta)
        {
            var index = _items.SelectedIndex; var target = index + delta;
            if (index < 0 || target < 0 || target >= _items.Items.Count) return;
            var text = _items.Items[index]; var check = _items.GetItemChecked(index);
            _items.Items.RemoveAt(index); _items.Items.Insert(target, text); _items.SetItemChecked(target, check); _items.SelectedIndex = target; Preview();
        }
    }
}
