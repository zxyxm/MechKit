using System;
using Environment = System.Environment;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MechKit.Core;
using SolidWorks.Interop.sldworks;

namespace MechKit.UI
{
    internal sealed class PartsLibraryPanel : UserControl
    {
        private readonly IAddinHost _host;
        private readonly QuickPartsLibrary _library;
        private readonly TextBox _search = new TextBox { Dock = DockStyle.Fill };
        private readonly ComboBox _category = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ListView _list = new ListView { Dock = DockStyle.Fill, View = System.Windows.Forms.View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
        private readonly Label _status = new Label { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 6, 0, 6) };
        private Point _start;
        private string _source;
        private IModelDoc2 _assembly;
        private IntPtr _viewHandle;
        private bool _dragging;
        private bool _reloading;
        private readonly Timer _dragTimer = new Timer { Interval = 40 };

        public PartsLibraryPanel(IAddinHost host) : this(host, new QuickPartsLibrary()) { }
        internal PartsLibraryPanel(IAddinHost host, QuickPartsLibrary library)
        {
            _host = host;
            _library = library;
            Dock = DockStyle.Fill;
            BackColor = Theme.Canvas;
            Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(10) };
            for (int i = 0; i < 7; i++) root.RowStyles.Add(new RowStyle(i == 5 ? SizeType.Percent : SizeType.AutoSize, i == 5 ? 100 : 0));
            root.Controls.Add(new Label { Text = "快捷零件库", AutoSize = true, Font = Theme.BodyBold, Margin = new Padding(0, 3, 0, 8) }, 0, 0);
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty };
            AddButton(actions, "打开库", () => WindowLayout.OpenExternal(new ProcessStartInfo(_library.Root) { UseShellExecute = true }));
            AddButton(actions, "添加零件进库", Import);
            AddButton(actions, "刷新", Reload);
            root.Controls.Add(actions, 0, 1);
            _category.Items.Add("全部分类");
            _category.Items.Add(QuickPartsLibrary.Uncategorized);
            _category.Items.AddRange(_library.GetCategories());
            _category.SelectedIndex = 0;
            root.Controls.Add(_category, 0, 2);
            var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            searchRow.Controls.Add(new Label { Text = "检索", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            searchRow.Controls.Add(_search, 1, 0);
            root.Controls.Add(searchRow, 0, 3);
            var hint = new Label { AutoSize = true, Dock = DockStyle.Fill, Text = "按住 Ctrl＋鼠标左键拖到装配体绘图区，松开鼠标复制并插入；Esc 或松开 Ctrl 取消。", ForeColor = Color.DimGray, Margin = new Padding(0, 6, 0, 8) };
            root.Controls.Add(hint, 0, 4);
            root.SizeChanged += (s, e) => { hint.MaximumSize = new Size(Math.Max(100, root.ClientSize.Width - 24), 0); _status.MaximumSize = hint.MaximumSize; };
            _list.Columns.Add("分类", 110);
            _list.Columns.Add("零件 / 型号", 230);
            _list.SizeChanged += (s, e) => _list.Columns[1].Width = Math.Max(100, _list.ClientSize.Width - _list.Columns[0].Width - 21);
            root.Controls.Add(_list, 0, 5);
            root.Controls.Add(_status, 0, 6);
            Controls.Add(root);
            _search.TextChanged += (s, e) => Reload();
            _category.SelectedIndexChanged += (s, e) => Reload();
            _list.MouseDown += StartDrag;
            _list.MouseMove += MoveDrag;
            _list.MouseUp += FinishDrag;
            // Native ListView/SW can take capture away before MouseMove/MouseUp.
            // Poll from mouse-down through release, even outside the task pane.
            _dragTimer.Tick += (s, e) => PollDrag();
            VisibleChanged += (s, e) => { if (!Visible) CancelDrag(); };
            Reload();
        }

        private void AddButton(Control parent, string text, Action action)
        {
            var button = Theme.CreateSecondaryButton(text);
            button.AutoSize = true;
            button.Click += (s, e) => { try { action(); } catch (Exception ex) { Report(ex); } };
            parent.Controls.Add(button);
        }

        private void Reload()
        {
            if (_reloading) return;
            CancelDrag();
            _reloading = true;
            try
            {
                var selected = Convert.ToString(_category.SelectedItem);
                _category.Items.Clear();
                _category.Items.Add("全部分类");
                _category.Items.Add(QuickPartsLibrary.Uncategorized);
                _category.Items.AddRange(_library.GetCategories());
                _category.SelectedIndex = Math.Max(0, _category.Items.IndexOf(selected));
                _list.BeginUpdate();
                _list.Items.Clear();
                foreach (var file in _library.Search(Convert.ToString(_category.SelectedItem), _search.Text))
                {
                    var item = new ListViewItem(_library.GetCategory(file)) { Tag = file, ToolTipText = file };
                    item.SubItems.Add(Path.GetFileNameWithoutExtension(file));
                    _list.Items.Add(item);
                }
                _list.ShowItemToolTips = true;
                _status.Text = _list.Items.Count == 0 ? "暂无匹配零件。点击“添加零件进库”导入 SW 2024 零件。" : "找到 " + _list.Items.Count + " 个零件 · 放置后可继续移动及配合";
            }
            catch (Exception ex) { Report(ex); }
            finally { _list.EndUpdate(); _reloading = false; }
        }

        private void Import()
        {
            using (var dialog = new OpenFileDialog { Title = "添加 SOLIDWORKS 2024 零件", Filter = "SOLIDWORKS 零件 (*.sldprt)|*.sldprt", Multiselect = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                using (var picker = new Form { Text = "选择入库分类", Width = 310, Height = 140, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
                {
                    var categories = new ComboBox { Left = 16, Top = 12, Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
                    categories.Items.Add(QuickPartsLibrary.Uncategorized);
                    categories.Items.AddRange(_library.GetCategories());
                    categories.SelectedIndex = Math.Max(0, categories.Items.IndexOf(Convert.ToString(_category.SelectedItem)));
                    var ok = new Button { Text = "添加进库", Left = 174, Top = 48, Width = 100, DialogResult = DialogResult.OK };
                    picker.Controls.Add(categories); picker.Controls.Add(ok); picker.AcceptButton = ok;
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    int imported = 0;
                    var errors = new System.Collections.Generic.List<string>();
                    foreach (var file in dialog.FileNames)
                    {
                        try { _library.Import(_host.SwApp, file, Convert.ToString(categories.SelectedItem)); imported++; }
                        catch (Exception ex) { errors.Add(Path.GetFileName(file) + "：" + ex.Message); }
                    }
                    Reload();
                    _status.Text = "已入库 " + imported + " 个零件";
                    if (errors.Count > 0) MessageBox.Show(this, string.Join(Environment.NewLine, errors), "未入库的零件", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void StartDrag(object sender, MouseEventArgs e)
        {
            CancelDrag();
            if (e.Button != MouseButtons.Left) return;
            if ((GetAsyncKeyState(17) & 0x8000) == 0)
            { _status.Text="按住 Ctrl＋鼠标左键拖动零件到装配体绘图区。"; return; }
            var item = _list.GetItemAt(e.X, e.Y);
            if (item == null) return;
            _source = item.Tag as string;
            _start = e.Location;
            _dragTimer.Start();
        }

        private void PollDrag()
        {
            if(_source==null) { _dragTimer.Stop();return; }
            if((GetAsyncKeyState(27)&0x8000)!=0 || (GetAsyncKeyState(17)&0x8000)==0)
            { CancelDrag();return; }
            if((GetAsyncKeyState(1)&0x8000)==0)
            { FinishDrag(_list,new MouseEventArgs(MouseButtons.Left,1,0,0,0));return; }
            var point=_list.PointToClient(Cursor.Position);
            MoveDrag(_list,new MouseEventArgs(MouseButtons.Left,0,point.X,point.Y,0));
        }

        private void MoveDrag(object sender, MouseEventArgs e)
        {
            if (_source == null || e.Button != MouseButtons.Left) return;
            if((GetAsyncKeyState(17)&0x8000)==0) { CancelDrag();return; }
            try
            {
                if (!_dragging)
                {
                    if (Math.Abs(e.X - _start.X) < SystemInformation.DragSize.Width && Math.Abs(e.Y - _start.Y) < SystemInformation.DragSize.Height) return;
                    _assembly = _host.SwApp.IActiveDoc2;
                    RequireAssembly(_assembly);
                    var view = _assembly.IActiveView;
                    _viewHandle = view == null ? IntPtr.Zero : new IntPtr(view.GetViewHWndx64());
                    _dragging = true;
                    _dragTimer.Start();
                    _status.Text = "保持 Ctrl，拖入绘图区后松开鼠标 · Esc 取消";
                }
                var over=IsOverView(Cursor.Position);
                _list.Cursor = over ? Cursors.Cross : Cursors.No;
                Cursor.Current = _list.Cursor;
                _status.Text=over ? "可以放置：保持 Ctrl，松开鼠标插入" : "正在拖动：移到目标装配体绘图区";
            }
            catch (Exception ex) { CancelDrag(); Report(ex); }
        }

        private static void RequireAssembly(IModelDoc2 document)
        {
            if (document == null || document.GetType() != SwUtils.DocAssembly)
                throw new InvalidOperationException("请先打开目标装配体。");
            if (string.IsNullOrEmpty(document.GetPathName()))
                throw new InvalidOperationException("请先保存装配体，才能将零件复制到装配体根目录。");
            if (((IAssemblyDoc)document).GetEditTargetComponent() != null)
                throw new InvalidOperationException("请退出零部件编辑状态后，再向根装配体拖入零件。");
        }

        private bool IsOverView(Point point)
        {
            var under = WindowFromPoint(point);
            return _viewHandle != IntPtr.Zero && (under == _viewHandle || IsChild(_viewHandle, under));
        }

        private void FinishDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var assembly = _assembly;
            var source = _source;
            var point = Cursor.Position;
            bool valid = false;
            try { valid = _dragging && (GetAsyncKeyState(17)&0x8000)!=0 && IsOverView(point)
                    && Equals(_host.SwApp.IActiveDoc2, assembly); }
            catch (Exception ex) { Report(ex); }
            finally { CancelDrag(); }
            if (!valid) return;
            try { Insert(assembly, source, point); }
            catch (Exception ex) { Report(ex); }
        }

        private void Insert(IModelDoc2 assembly, string source, Point screen)
        {
            RequireAssembly(assembly);
            var sw = _host.SwApp;
            QuickPartsLibrary.Validate(sw, source);
            var view = assembly.IActiveView;
            if (view.HasPerspective()) throw new InvalidOperationException("请关闭透视显示后拖入零件，以保证放置位置准确。");
            if (!ScreenToClient(new IntPtr(view.GetViewHWndx64()), ref screen)) throw new InvalidOperationException("无法获取绘图区位置。");
            var math = (IMathUtility)sw.GetMathUtility();
            var transform = view.Transform;
            var origin = (IMathPoint)math.CreatePoint(new double[] { 0, 0, 0 });
            var projected = (double[])((IMathPoint)origin.MultiplyTransform(transform)).ArrayData;
            var pixel = (IMathPoint)math.CreatePoint(new double[] { screen.X, screen.Y, projected[2] });
            var location = (double[])((IMathPoint)pixel.MultiplyTransform(transform.Inverse())).ArrayData;
            string copy = null;
            IModelDoc2 loaded = null;
            bool inserted = false;
            try
            {
                // No filesystem mutation before the mouse is released over a valid assembly view.
                copy = QuickPartsLibrary.CopyUnique(source, Path.GetDirectoryName(assembly.GetPathName()));
                int errors = 0, warnings = 0;
                loaded = sw.OpenDoc6(copy, SwUtils.DocPart, 1, "", ref errors, ref warnings);
                if (loaded == null) throw new InvalidOperationException("无法打开零件副本，SW 错误码：" + errors);
                if (!string.Equals(loaded.GetPathName(), copy, StringComparison.OrdinalIgnoreCase))
                {
                    loaded = null; // Never close an unrelated document returned by SW.
                    throw new InvalidOperationException("已打开同名模型，请关闭同名模型后重试。");
                }
                sw.ActivateDoc3(assembly.GetPathName(), false, 0, ref errors);
                if (!Equals(sw.IActiveDoc2, assembly)) throw new InvalidOperationException("无法激活目标装配体。");
                var component = ((IAssemblyDoc)assembly).AddComponent5(copy, 0, "", false, "", location[0], location[1], location[2]);
                if (component == null) throw new InvalidOperationException("SW 未能插入零件副本。");
                inserted = true;
                assembly.GraphicsRedraw2();
                WindowLayout.YieldToOpenedContent(_host.MainWindowHandle);
                _status.Text = "已插入：" + Path.GetFileName(copy) + "（装配体根目录）";
                Log.Info("快捷零件库：" + source + " → " + copy);
            }
            finally
            {
                try
                {
                    if (loaded != null) sw.CloseDoc(copy);
                    int activateError = 0;
                    sw.ActivateDoc3(assembly.GetPathName(), false, 0, ref activateError);
                }
                catch (Exception ex) { Log.Warn("恢复装配体视图失败：" + ex.Message); }
                if (!inserted && copy != null)
                {
                    try { File.Delete(copy); }
                    catch (Exception ex) { Log.Warn("插入失败，副本无法清理：" + copy + "；" + ex.Message); }
                }
            }
        }

        private void CancelDrag()
        {
            var wasDragging = _dragging;
            _dragging = false; _source = null; _assembly = null;
            _viewHandle = IntPtr.Zero;
            _dragTimer.Stop();
            _list.Capture = false;
            _list.Cursor = Cursors.Default;
            Cursor.Current = Cursors.Default;
            if (wasDragging) _status.Text = "拖动结束 · 可继续选择零件";
        }
        private void Report(Exception ex) { _status.Text = ex.Message; Log.Error("快捷零件库：" + ex.Message, ex); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { CancelDrag(); _dragTimer.Dispose(); }
            base.Dispose(disposing);
        }
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    }
}
