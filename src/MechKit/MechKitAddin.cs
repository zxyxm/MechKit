using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using MechKit.Core;
using MechKit.Features;
using MechKit.UI;

namespace MechKit
{
    /// <summary>
    /// 插件入口。
    /// SOLIDWORKS 通过注册表里的 CLSID 创建本对象，并调用 ISwAddin 的两个方法。
    /// 命令回调方法（OnXxx）通过 IDispatch 按方法名调用，因此必须是 public 且方法名与
    /// AddCommandItem2 里登记的名字完全一致。
    /// </summary>
    [Guid(AddinConstants.AddinGuid)]
    [ProgId(AddinConstants.ProgId)]
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public class MechKitAddin : ISwAddin, IAddinHost
    {
        private ISldWorks _swApp;
        private int _cookie;
        private CommandManager _commandManager;
        private CommandGroup _commandGroup;
        private CommandGroupLayout _layout;
        private int _commandGroupId = AddinConstants.CommandGroupId;
        private int _nextCommandGroupId = AddinConstants.CommandGroupId + 1;
        private bool _repairingCommandTab;
        private bool _activateCommandTabOnNextDocument = true;
        private ITaskpaneView _taskPaneView;
        private TaskPaneControl _taskPane;
        private AddinSettings _settings;
        private SldWorks _events;
        private bool _disconnecting;
        private string _lastNamingUndoDocument;
        private bool _lastNamingUndoCreatedFile;
        private readonly Dictionary<string, Form> _openForms =
            new Dictionary<string, Form>(StringComparer.OrdinalIgnoreCase);

        public MechKitAddin()
        {
            _settings = new AddinSettings();
        }

        #region ISwAddin

        public bool ConnectToSW(object thisSw, int cookie)
        {
            // 最先写日志：这样即使后面失败，也能确定 SOLIDWORKS 确实调用了插件
            Log.Info("[connect] 收到 SOLIDWORKS 的加载请求。");

            try
            {
                _swApp = thisSw as ISldWorks;
                if (_swApp == null)
                {
                    Log.Error(string.Format("[connect] thisSw 无法转换为 ISldWorks（实际类型：{0}）",
                        thisSw == null ? "null" : thisSw.GetType().FullName));
                    return false;
                }

                Log.Info("[connect] 已取得 ISldWorks 接口。");
                _cookie = cookie;
                _settings = AddinSettings.Load();

                // 注册回调对象：SOLIDWORKS 通过它按名字调用 OnXxx / EnableXxx
                _swApp.SetAddinCallbackInfo2(0, this, _cookie);

                IconResources.Extract();
                CreateCommandGroup();
                EnsureActiveDocumentCommandTab();
                CreateTaskPane();
                AttachEvents();

                // "[connect]" / "[disconnect]" 是给安装脚本和排错用的 ASCII 标记
                Log.Info(string.Format("[connect] MechKit v{0} 已加载（SOLIDWORKS {1}）",
                    AddinVersion, _swApp.RevisionNumber()));
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("插件加载失败", ex);
                MessageBox.Show("MechKit加载失败：" + ex.Message, AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            _disconnecting = true;

            try
            {
                CloseOpenForms();
                DetachEvents();

                if (_taskPane != null)
                {
                    _taskPane.Dispose();
                    _taskPane = null;
                }

                if (_taskPaneView != null)
                {
                    try
                    {
                        _taskPaneView.DeleteView();
                    }
                    catch
                    {
                        // 视图可能已被用户关闭
                    }

                    Marshal.ReleaseComObject(_taskPaneView);
                    _taskPaneView = null;
                }

                if (_commandManager != null)
                {
                    // 只移除本次运行的 COM 命令对象，保留注册表中的 CommandManager
                    // 选项卡布局，避免正常关闭 SOLIDWORKS 后选项卡消失。
                    RemoveCommandGroup(_commandGroupId);
                    Marshal.ReleaseComObject(_commandManager);
                    _commandManager = null;
                }

                _commandGroup = null;
                _layout = null;

                if (_settings != null)
                {
                    _settings.Save();
                }

                if (_swApp != null)
                {
                    Marshal.ReleaseComObject(_swApp);
                    _swApp = null;
                }

                Log.Info("[disconnect] MechKit 已卸载。");
            }
            catch (Exception ex)
            {
                Log.Error("卸载插件时出错", ex);
            }
            finally
            {
                // SOLIDWORKS 要求插件在这里彻底释放托管引用
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            return true;
        }

        #endregion

        #region COM 注册

        [ComRegisterFunction]
        public static void RegisterFunction(Type type)
        {
            RegisterAddin(type, true);
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type type)
        {
            UnregisterAddin(type);
        }

        /// <summary>
        /// 写入 SOLIDWORKS 识别插件所需的两处注册表项：
        ///   HKLM\SOFTWARE\SolidWorks\AddIns\{GUID}          —— 插件名称与说明
        ///   HKCU\Software\SolidWorks\AddInsStartup\{GUID}   —— 是否随 SOLIDWORKS 启动
        /// 没有管理员权限时自动退回当前用户配置单元。
        /// </summary>
        public static void RegisterAddin(Type type, bool loadAtStartup)
        {
            var guid = "{" + type.GUID.ToString().ToUpperInvariant() + "}";

            var writtenToMachine = false;
            try
            {
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\SolidWorks\AddIns\" + guid))
                {
                    if (key != null)
                    {
                        key.SetValue(null, 1, RegistryValueKind.DWord);
                        key.SetValue("Title", AddinConstants.Title);
                        key.SetValue("Description", AddinConstants.Description);
                        writtenToMachine = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("写入 HKLM 插件注册项失败，改用当前用户：" + ex.Message);
            }

            if (!writtenToMachine)
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\SolidWorks\AddIns\" + guid))
                {
                    if (key != null)
                    {
                        key.SetValue(null, 1, RegistryValueKind.DWord);
                        key.SetValue("Title", AddinConstants.Title);
                        key.SetValue("Description", AddinConstants.Description);
                    }
                }
            }

            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\SolidWorks\AddInsStartup\" + guid))
            {
                if (key != null)
                {
                    key.SetValue(null, loadAtStartup ? 1 : 0, RegistryValueKind.DWord);
                }
            }
        }

        public static void UnregisterAddin(Type type)
        {
            var guid = "{" + type.GUID.ToString().ToUpperInvariant() + "}";
            DeleteKey(Registry.LocalMachine, @"SOFTWARE\SolidWorks\AddIns\" + guid);
            DeleteKey(Registry.CurrentUser, @"SOFTWARE\SolidWorks\AddIns\" + guid);
            DeleteKey(Registry.CurrentUser, @"Software\SolidWorks\AddInsStartup\" + guid);
        }

        private static void DeleteKey(RegistryKey root, string path)
        {
            try
            {
                root.DeleteSubKey(path, false);
            }
            catch (Exception ex)
            {
                Log.Warn(string.Format("删除注册表项 {0}\\{1} 失败：{2}", root.Name, path, ex.Message));
            }
        }

        #endregion

        #region IAddinHost

        public ISldWorks SwApp
        {
            get { return _swApp; }
        }

        public AddinSettings Settings
        {
            get { return _settings; }
        }

        public IntPtr MainWindowHandle
        {
            get
            {
                try
                {
                    var frame = _swApp == null ? null : _swApp.IFrameObject();
                    return frame == null ? IntPtr.Zero : new IntPtr(frame.GetHWndx64());
                }
                catch
                {
                    return IntPtr.Zero;
                }
            }
        }

        /// <summary>当前文档的三维图形区窗口句柄；没有文档时返回零。</summary>
        private IntPtr ActiveViewWindowHandle()
        {
            try
            {
                var doc = _swApp == null ? null : _swApp.IActiveDoc2;
                var view = doc == null ? null : doc.IActiveView;
                return view == null ? IntPtr.Zero : new IntPtr(view.GetViewHWndx64());
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        public void ShowBatchExportDialog()
        {
            ShowBatchExportDialog(null);
        }

        /// <summary>
        /// 打开批量导出。files 非空时（从 BOM 表勾选行带入）直接放进待导出列表，
        /// 只需要选格式和输出目录即可开始。
        /// </summary>
        public void ShowBatchExportDialog(IList<string> files)
        {
            ShowModeless("batch-export", delegate { return new BatchExportForm(this, files); });
        }

        private IList<string> _bomSelectedFiles = new List<string>();

        /// <summary>BOM 表格里选中的零件文件；非空时，快捷命名按钮直接作用于它们。</summary>
        public void SetBomSelectedFiles(IList<string> filePaths)
        {
            _bomSelectedFiles = filePaths == null ? new List<string>() : new List<string>(filePaths);
        }

        /// <summary>
        /// 一键生成 BOM：汇总当前装配体（全部零件，含标准件/外购件），
        /// 导出 CSV 并直接打开，让 Excel 立即可用。
        /// </summary>
        public void GenerateBom()
        {
            try
            {
                var doc = SwUtils.ActiveDoc(_swApp);
                if (doc == null)
                {
                    MessageBox.Show("请先打开一个装配体或零件。", AddinConstants.Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var options = BuildBomOptions();
                var rows = new List<PartListRow>();
                var title = SwUtils.BomFileName(doc);

                if (doc.GetType() == SwUtils.DocAssembly)
                {
                    var assembly = doc as AssemblyDoc;
                    if (assembly == null)
                    {
                        Log.Error("无法访问装配体接口。");
                        return;
                    }

                    rows = PartListService.FromAssembly(_swApp, assembly, options, delegate { });
                }
                else if (doc.GetType() == SwUtils.DocPart)
                {
                    var row = PartListService.FromPart(_swApp, doc, options);
                    if (row != null)
                    {
                        rows.Add(row);
                    }
                }
                else
                {
                    MessageBox.Show("当前文档不是零件或装配体。", AddinConstants.Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (rows.Count == 0)
                {
                    MessageBox.Show("没有可汇总的零件。", AddinConstants.Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var folder = ResolveBomFolder(doc);
                var path = PartListService.ExportCsv(rows, folder, title, options);
                var summary = PartListService.BuildSummary(rows);

                _settings.OutputFolder = folder;
                _settings.Save();

                Log.Info(string.Format("BOM 已生成（{0} 项）：{1}", rows.Count, path));

                // 一键体验：生成后直接打开，Excel 里立刻能用
                try
                {
                    Process.Start(path);
                }
                catch (Exception ex)
                {
                    Log.Warn("自动打开 BOM 失败：" + ex.Message);
                }

                _taskPane?.RefreshDocument(false);

                MessageBox.Show(
                    string.Format("BOM 已生成：\r\n{0}\r\n\r\n共 {1} 项，{2}",
                        path, rows.Count, summary),
                    AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log.Error("生成 BOM 失败", ex);
                MessageBox.Show("生成 BOM 失败：" + ex.Message, AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private PartListOptions BuildBomOptions()
        {
            var options = new PartListOptions
            {
                OnlyMachined = false,        // BOM 需要完整清单
                ExcludeToolbox = false,      // 标准件也要列出
                DetectVendorParts = _settings.DetectVendorParts,
                ExcludeSuppressed = true,
                ReadCustomProperties = _settings.BomUsePropertyFields && _settings.PartListReadProperties,
                AssemblyLevel = _settings.BomAssemblyLevel,
                StandardNameField = _settings.BomStandardNameField,
                StandardMaterialField = _settings.BomStandardMaterialField,
                StandardProcessField = _settings.BomStandardProcessField,
                StandardRemarkField = _settings.BomStandardRemarkField,
                MachinedNameField = _settings.BomMachinedNameField,
                MachinedMaterialField = _settings.BomMachinedMaterialField,
                MachinedProcessField = _settings.BomMachinedProcessField,
                MachinedRemarkField = _settings.BomMachinedRemarkField,
                SequenceHeader = _settings.BomSequenceHeader,
                LocationHeader = _settings.BomLocationHeader,
                FullNameHeader = _settings.BomFullNameHeader,
                ClassificationHeader = _settings.BomClassificationHeader,
                NameHeader = _settings.BomNameHeader,
                MaterialHeader = _settings.BomMaterialHeader,
                ProcessHeader = _settings.BomProcessHeader,
                SurfaceHeader = _settings.BomSurfaceHeader,
                QuantityHeader = _settings.BomQuantityHeader,
                AssemblyNoteHeader = _settings.BomAssemblyNoteHeader,
                RemarkHeader = _settings.BomRemarkHeader,
                ColumnOrder = _settings.BomColumnOrder,
                StandardAssemblyNoteField = _settings.BomStandardAssemblyNoteField,
                MachinedAssemblyNoteField = _settings.BomMachinedAssemblyNoteField,
                StandardSurfaceField = _settings.BomStandardSurfaceField,
                MachinedSurfaceField = _settings.BomMachinedSurfaceField
            };

            options.Naming = NamingOptionsFactory.FromSettings(_settings);
            return options;
        }

        private string ResolveBomFolder(ModelDoc2 doc)
        {
            var configured = _settings.OutputFolder;
            if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured))
            {
                return configured;
            }

            try
            {
                var path = doc.GetPathName();
                if (!string.IsNullOrEmpty(path))
                {
                    var folder = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    {
                        return folder;
                    }
                }
            }
            catch
            {
                // 未保存的文档没有路径
            }

            return AppPaths.Ensure(AppPaths.Root);
        }

        public void ShowPartListDialog()
        {
            try
            {
                Log.Info("打开明细汇总窗口。");
                ShowModeless("part-list", delegate { return new PartListForm(this); });
            }
            catch (Exception ex)
            {
                Log.Error("打开明细汇总窗口失败", ex);
                MessageBox.Show("无法打开明细汇总：" + ex.Message, AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void ShowSettingsDialog()
        {
            ShowSettingsDialog(0);
        }

        /// <summary>打开设置；tabIndex 0 = BOM 格式，1 = 个人配置。</summary>
        public void ShowSettingsDialog(int tabIndex)
        {
            var safeTab = tabIndex == 1 ? 1 : 0;
            ShowModeless("settings", delegate { return new SettingsForm(this, safeTab); });
        }

        /// <summary>打开命名规则设置：0 = 加工件栏，1 = 标准件栏。</summary>
        public void ShowNamingRuleDialog(int tabIndex)
        {
            ShowNamingRuleDialog(tabIndex, null);
        }

        public void ShowNamingRuleDialog(int tabIndex, Action onClosed)
        {
            var safeTab = tabIndex == 1 ? 1 : 0;
            ShowModeless("naming-" + safeTab,
                delegate { return new NamingRuleForm(this, safeTab); }, onClosed);
        }

        /// <summary>
        /// 保存命名规则后立即按最新设置重建 MechKit 选项卡上的快捷按钮，
        /// 不需要重启 SOLIDWORKS。
        ///
        /// SOLIDWORKS 规定：命令按钮集合发生变化时必须换用新的命令组 UserID，
        /// 否则会沿用上一次的按钮布局缓存。因此这里按顺序：
        /// ① 用新 UserID 注册新命令组；② 移除旧选项卡（按钮引用的旧命令组随即失效）；
        /// ③ 用新命令组重建三种文档类型的选项卡；④ 删除旧命令组。
        /// 任何一步失败都会保留原来的选项卡和命令组，避免整个 MechKit 标签消失。
        /// </summary>
        public void RefreshNamingCommands()
        {
            var tabWasActive = IsMechKitTabActive();

            try
            {
                if (_commandManager == null || _layout == null)
                {
                    return;
                }

                var previousGroupId = _layout.GroupId;
                var previousGroup = _layout.Group;
                var layout = BuildCommandGroup(NextCommandGroupId(), false);
                if (layout == null)
                {
                    // 新命令组没建成：保留旧命令组与旧选项卡，界面维持原样。
                    Log.Warn("命名快捷按钮刷新失败，已保留原有 MechKit 选项卡。");
                    WarnRefreshFailed();
                    return;
                }

                ApplyLayout(layout);
                PreserveToolbarVisibility(previousGroup, layout.Group);
                RemoveCommandTabs();
                CreateCommandTabs(layout, true);

                if (!CommandTabsReady())
                {
                    Log.Warn("重建后的 MechKit 选项卡校验未通过，正在重试。");
                    RemoveCommandTabs();
                    CreateCommandTabs(layout, true);
                }

                // 选项卡已经改用新命令组，旧命令组可以安全删除，避免重复的菜单/工具栏。
                if (previousGroupId != layout.GroupId)
                {
                    RemoveCommandGroup(previousGroupId);
                }

                RefreshTaskPaneShortcuts();

                if (!CommandTabsReady())
                {
                    Log.Error("命名快捷按钮刷新后仍未找到完整的 MechKit 选项卡。");
                    WarnRefreshFailed();
                    return;
                }

                // 用户正停在 MechKit 选项卡上时重新激活一次，让按钮立刻重画。
                if (tabWasActive)
                {
                    ActivateCommandTab();
                }

                Log.Info(string.Format(
                    "命名快捷按钮已按最新设置刷新（命令组 {0} → {1}）。", previousGroupId, layout.GroupId));
            }
            catch (Exception ex)
            {
                // 设置已经落盘，命令回调读取的都是最新设置；最坏情况只是按钮要等到
                // 下次启动 SOLIDWORKS 才更新，所以这里不再删除命令组作为恢复手段。
                Log.Error("刷新 MechKit 选项卡失败", ex);
                WarnRefreshFailed();
            }
        }

        /// <summary>
        /// 给选中的组件加/去命名前缀。新名称统一用短横线分段，读取兼容旧下划线，
        /// 只有符合该规则的零件才会进入 BOM。
        /// </summary>
        public void ApplyPrefix(string prefix, bool remove)
        {
            try
            {
                // 设置页保存的前缀都视为“已知前缀”。从面板切换前缀时替换旧值，
                // 不叠加成“气动_电机_”。
                var knownList = new List<string>(NamingOptionsFactory.ParsePrefixes(_settings.BomPrefixes));
                if (!knownList.Contains("参考"))
                {
                    knownList.Add("参考");
                }
                var known = knownList.ToArray();
                RenameSelectedComponentFiles(delegate(string name)
                {
                    var effectivePrefix = prefix;
                    if (!remove && _settings.StandardPrefixBindingEnabled)
                    {
                        var middle = FindMiddleName(name, known,
                            NamingOptionsFactory.ParsePrefixes(_settings.BomMiddleNames));
                        string required;
                        if (NamingOptionsFactory.ParsePrefixBindings(_settings.StandardPrefixBindings)
                            .TryGetValue(middle, out required))
                        {
                            effectivePrefix = required;
                        }
                    }
                    return remove ? StripPrefix(name, known) : AddPrefix(name, effectivePrefix, known);
                }, remove ? "去掉前缀" : "添加前缀");
            }
            catch (Exception ex)
            {
                Log.Error("加前缀失败", ex);
            }
        }

        private static string AddPrefix(string name, string prefix, string[] known)
        {
            var value = (prefix ?? string.Empty).Trim().TrimEnd('_', '-', '*', '＊');
            if (value.Length == 0)
            {
                return name;
            }

            return value + "-" + StripPrefix(name, known).Replace('_', '-').TrimStart('-');
        }

        private static string StripPrefix(string name, string[] known)
        {
            if (string.IsNullOrEmpty(name) || known == null)
            {
                return name;
            }

            foreach (var prefix in known)
            {
                if (string.IsNullOrEmpty(prefix))
                {
                    continue;
                }

                foreach (var separator in new[] { '-', '_' })
                {
                    var withSeparator = prefix.Trim() + separator;
                    if (name.StartsWith(withSeparator, StringComparison.OrdinalIgnoreCase))
                    {
                        return name.Substring(withSeparator.Length);
                    }
                }
            }

            return name;
        }

        /// <summary>把选中组件改成“前缀-中文中间名-原始名称或型号”。</summary>
        public void ApplyMiddleName(string middleName, bool remove)
        {
            try
            {
                var prefixes = NamingOptionsFactory.ParsePrefixes(_settings.BomPrefixes);
                var knownMiddleNames = NamingOptionsFactory.ParsePrefixes(_settings.BomMiddleNames);
                RenameSelectedComponentFiles(delegate(string name)
                {
                    var preparedName = name;
                    if (!remove && _settings.StandardPrefixBindingEnabled)
                    {
                        string requiredPrefix;
                        if (NamingOptionsFactory.ParsePrefixBindings(_settings.StandardPrefixBindings)
                            .TryGetValue((middleName ?? string.Empty).Trim(), out requiredPrefix))
                        {
                            preparedName = AddPrefix(preparedName, requiredPrefix, prefixes);
                        }
                    }
                    return SetMiddleName(preparedName, middleName, prefixes, knownMiddleNames, remove);
                }, remove ? "去掉中间名" : "设置中间名");
            }
            catch (Exception ex)
            {
                Log.Error("设置中间名失败", ex);
            }
        }

        /// <summary>
        /// 使用 SOLIDWORKS 的 RenameDocument 重命名选中组件引用的文件。
        /// 用户也可以先 MakeIndependent：该模式会在原目录新建文件，并只替换当前实例。
        /// </summary>
        /// <summary>按文件路径在当前装配体里找组件（含子装配体内部的零件）。</summary>
        private static List<Component2> FindComponentsByPaths(AssemblyDoc assembly, IList<string> paths)
        {
            var result = new List<Component2>();
            if (assembly == null || paths == null || paths.Count == 0)
            {
                return result;
            }

            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    wanted.Add(path.Trim());
                }
            }

            try
            {
                var components = assembly.GetComponents(true) as object[];
                if (components == null)
                {
                    return result;
                }

                foreach (var item in components)
                {
                    var component = item as Component2;
                    if (component == null)
                    {
                        continue;
                    }

                    try
                    {
                        var path = component.GetPathName() ?? string.Empty;
                        if (path.Length > 0 && wanted.Contains(path))
                        {
                            result.Add(component);
                        }
                    }
                    catch
                    {
                        // 跳过读不到路径的组件。
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("按 BOM 选择查找组件失败：" + ex.Message);
            }

            return result;
        }

        private void RenameSelectedComponentFiles(Func<string, string> transform, string actionName)
        {
            var doc = SwUtils.ActiveDoc(_swApp);
            var assembly = doc as AssemblyDoc;
            if (doc == null || assembly == null)
            {
                MessageBox.Show("请先打开装配体，并在树里或图形区选中零件 / 子装配体。",
                    AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selection = doc.SelectionManager as SelectionMgr;
            var selectedCount = selection == null ? 0 : selection.GetSelectedObjectCount2(-1);
            var components = new List<Component2>();

            // 优先使用 BOM 表格里选中的行：在表格里选中零件后直接点选项卡按钮即可改名，
            // 不必先在装配树 / 图形区重新选一遍。
            var bomFiles = _bomSelectedFiles;
            if (bomFiles != null && bomFiles.Count > 0)
            {
                components.AddRange(FindComponentsByPaths(assembly, bomFiles));
                if (components.Count == 0)
                {
                    MessageBox.Show(
                        "BOM 表里选中的零件在当前装配体里找不到，请改在装配树或图形区里选中它们。",
                        AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            else
            {
                for (var index = 1; index <= selectedCount; index++)
                {
                    try
                    {
                        var component = selection.GetSelectedObject6(index, -1) as Component2;
                        if (component != null)
                        {
                            components.Add(component);
                        }
                    }
                    catch
                    {
                        // 非组件选择不参与文件重命名。
                    }
                }
            }

            if (components.Count == 0)
            {
                MessageBox.Show("请先选中要处理的零件或子装配体。", AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var mode = MessageBox.Show(
                "请选择重命名方式：\r\n\r\n" +
                "“是”＝先使所选组件独立，再修改名称（会在原目录新建一个零件文件，只影响当前实例）\r\n" +
                "“否”＝直接修改原零件文件名（引用该文件的组件会一起更新）\r\n" +
                "“取消”＝不修改",
                actionName + " - " + AddinConstants.Title,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (mode == DialogResult.Cancel)
            {
                return;
            }

            var makeIndependent = mode == DialogResult.Yes;
            var renamed = 0;
            var independent = 0;
            var skipped = 0;
            var failures = new List<string>();
            var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var recordingUndo = false;
            _lastNamingUndoDocument = null;
            _lastNamingUndoCreatedFile = false;
            try
            {
                doc.Extension.StartRecordingUndoObject();
                recordingUndo = true;
            }
            catch (Exception ex)
            {
                Log.Warn("开始记录快捷命名撤回点失败：" + ex.Message);
            }

            foreach (var component in components)
            {
                string sourcePath;
                try { sourcePath = component.GetPathName() ?? string.Empty; }
                catch { sourcePath = string.Empty; }
                if (sourcePath.Length == 0)
                {
                    failures.Add("未保存或虚拟组件无法用文件方式重命名。");
                    continue;
                }

                if (!makeIndependent && !processedFiles.Add(sourcePath))
                {
                    skipped++;
                    continue;
                }

                var currentName = Path.GetFileNameWithoutExtension(sourcePath);
                var targetName = (transform(currentName) ?? string.Empty).Trim();
                if (targetName.Length == 0 || string.Equals(currentName, targetName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                if (targetName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    failures.Add("名称含有文件名不允许的字符：" + targetName);
                    continue;
                }

                try
                {
                    var suppression = component.GetSuppression();
                    if (suppression == (int)swComponentSuppressionState_e.swComponentLightweight ||
                        suppression == (int)swComponentSuppressionState_e.swComponentFullyLightweight)
                    {
                        component.SetSuppression2((int)swComponentSuppressionState_e.swComponentResolved);
                    }

                    doc.ClearSelection2(true);
                    if (!component.Select4(false, null, false))
                    {
                        failures.Add("无法选择组件：" + currentName);
                        continue;
                    }

                    if (makeIndependent)
                    {
                        var targetPath = Path.Combine(Path.GetDirectoryName(sourcePath) ?? string.Empty,
                            targetName + Path.GetExtension(sourcePath));
                        if (File.Exists(targetPath))
                        {
                            failures.Add("目标文件已存在：" + targetPath);
                            continue;
                        }

                        if (!assembly.MakeIndependent(targetPath))
                        {
                            failures.Add("无法使组件独立：" + currentName);
                            continue;
                        }

                        independent++;
                        renamed++;
                        Log.Info(string.Format("组件已独立并新建文件：{0} → {1}", sourcePath, targetPath));
                    }
                    else
                    {
                        var error = doc.Extension.RenameDocument(targetName);
                        if (error != (int)swRenameDocumentError_e.swRenameDocumentError_None)
                        {
                            failures.Add(string.Format("{0} → {1} 失败（错误 {2}：{3}）",
                                currentName, targetName, error, DescribeRenameError(error)));
                            continue;
                        }

                        renamed++;
                        Log.Info(string.Format("零件文件重命名：{0} → {1}", sourcePath, targetName));
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(currentName + "：" + ex.Message);
                    Log.Warn("零件文件重命名失败：" + ex.Message);
                }
            }

            if (recordingUndo)
            {
                try
                {
                    if (doc.Extension.FinishRecordingUndoObject("MechKit 快捷命名") && renamed > 0)
                    {
                        _lastNamingUndoDocument = doc.GetPathName() ?? doc.GetTitle();
                        _lastNamingUndoCreatedFile = independent > 0;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("结束记录快捷命名撤回点失败：" + ex.Message);
                }
            }

            if (renamed > 0)
            {
                try
                {
                    doc.EditRebuild3();
                    int saveErrors = 0;
                    int saveWarnings = 0;
                    var saveOptions = (int)(swSaveAsOptions_e.swSaveAsOptions_Silent |
                        swSaveAsOptions_e.swSaveAsOptions_SaveReferenced);
                    if (!doc.Save3(saveOptions,
                            ref saveErrors, ref saveWarnings) || saveErrors != 0)
                    {
                        failures.Add("名称已修改，但装配体保存失败（错误 " + saveErrors + "）。");
                    }
                }
                catch (Exception ex)
                {
                    failures.Add("名称已修改，但装配体保存失败：" + ex.Message);
                }
            }

            var message = string.Format("{0}完成。\r\n\r\n已修改零件文件：{1} 个", actionName, renamed);
            if (independent > 0)
            {
                message += string.Format("\r\n其中先使之独立：{0} 个\r\n\r\n注意：独立模式本质上新建了零件文件，当前装配体已改为引用新文件。",
                    independent);
            }
            if (skipped > 0)
            {
                message += "\r\n无需重复修改：" + skipped + " 个";
            }
            if (failures.Count > 0)
            {
                message += "\r\n\r\n未完成：\r\n- " + string.Join("\r\n- ", failures.ToArray());
            }

            // 快捷命名会高频使用，完成报告不再弹出并阻塞后续操作。
            // 结果和失败原因保留在日志中，便于需要时排查。
            if (failures.Count == 0)
            {
                Log.Info(message.Replace("\r\n", " "));
            }
            else
            {
                Log.Warn(message.Replace("\r\n", " "));
            }
        }

        private static string DescribeRenameError(int error)
        {
            switch ((swRenameDocumentError_e)error)
            {
                case swRenameDocumentError_e.swRenameDocumentError_ComponentNotResolved: return "组件未解析";
                case swRenameDocumentError_e.swRenameDocumentError_LightWeightComponent: return "轻化组件尚未完全解析";
                case swRenameDocumentError_e.swRenameDocumentError_NoModelLoaded: return "组件模型尚未载入";
                case swRenameDocumentError_e.swRenameDocumentError_FileAlreadyExists: return "目标文件已存在";
                case swRenameDocumentError_e.swRenameDocumentError_InvalidCharactersInName: return "名称含无效字符";
                case swRenameDocumentError_e.swRenameDocumentError_ReadOnlyDocument: return "文件或引用为只读";
                case swRenameDocumentError_e.swRenameDocumentError_DocumentNameInUse: return "目标名称正在使用";
                case swRenameDocumentError_e.swRenameDocumentError_ToolboxComponent: return "Toolbox 组件不能这样重命名";
                case swRenameDocumentError_e.swRenameDocumentError_PatternedComponent: return "阵列组件不能这样重命名";
                default: return "请确认组件已解析，且已允许从 FeatureManager 树重命名组件文件";
            }
        }

        private static string SetMiddleName(string name, string middleName, string[] prefixes,
            string[] knownMiddleNames, bool remove)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            var prefix = string.Empty;
            var remainder = name;
            foreach (var item in prefixes ?? new string[0])
            {
                var token = (item ?? string.Empty).Trim();
                var markerLength = LeadingTokenLength(remainder, token);
                if (markerLength > 0)
                {
                    prefix = token;
                    remainder = remainder.Substring(markerLength);
                    break;
                }
            }

            foreach (var item in knownMiddleNames ?? new string[0])
            {
                var markerLength = LeadingTokenLength(remainder, (item ?? string.Empty).Trim());
                if (markerLength > 0)
                {
                    remainder = remainder.Substring(markerLength);
                    break;
                }
            }

            var value = remove ? string.Empty : (middleName ?? string.Empty).Trim().Trim('_', '-');
            var parts = new List<string>();
            if (prefix.Length > 0) parts.Add(prefix);
            if (value.Length > 0) parts.Add(value);
            if (remainder.Length > 0) parts.Add(remainder.Replace('_', '-').Trim('-'));
            return string.Join("-", parts.ToArray());
        }

        private static int LeadingTokenLength(string value, string token)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(token))
            {
                return 0;
            }
            foreach (var separator in new[] { '-', '_' })
            {
                var marker = token + separator;
                if (value.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return marker.Length;
                }
            }
            return 0;
        }

        private static string FindMiddleName(string name, string[] prefixes, string[] middleNames)
        {
            var remainder = StripPrefix(name, prefixes);
            foreach (var middle in middleNames ?? new string[0])
            {
                var marker = (middle ?? string.Empty).Trim();
                if (string.Equals(remainder, marker, StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith(marker + "_", StringComparison.OrdinalIgnoreCase) ||
                    remainder.StartsWith(marker + "-", StringComparison.OrdinalIgnoreCase))
                {
                    return marker;
                }
            }
            return string.Empty;
        }

        public void ShowPropertyToolDialog()
        {
            ShowModeless("property-tool", delegate { return new PropertyToolForm(this); });
        }

        public void ShowAboutDialog()
        {
            ShowModeless("about", delegate { return new AboutForm(this); });
        }

        public void ToggleTaskPane()
        {
            try
            {
                if (_taskPaneView == null)
                {
                    CreateTaskPane();
                }

                if (_taskPaneView == null)
                {
                    MessageBox.Show("任务面板不可用。", AddinConstants.Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (_taskPaneView.IsActiveTab())
                {
                    _taskPaneView.HideView();
                }
                else
                {
                    _taskPaneView.ShowView();
                }
            }
            catch (Exception ex)
            {
                Log.Error("切换任务面板失败", ex);
            }
        }

        #endregion

        #region 命令回调（方法名必须与 AddCommandItem2 登记的一致）

        public void OnGenerateBom()
        {
            GenerateBom();
        }

        public void OnSettings()
        {
            ShowSettingsDialog();
        }

        public void OnPartNamingRule()
        {
            ShowNamingRuleDialog(0);
        }

        public void OnStandardPrefix()
        {
            ShowNamingRuleDialog(1);
        }

        /// <summary>参考件快捷按钮：增加“参考-”前缀，并替换已有标准件/参考件前缀。</summary>
        public void OnReferencePartCommand()
        {
            try
            {
                var known = new List<string>(NamingOptionsFactory.ParsePrefixes(_settings.BomPrefixes));
                if (!known.Contains("参考"))
                {
                    known.Add("参考");
                }
                RenameSelectedComponentFiles(delegate(string name)
                {
                    return AddPrefix(name, "参考", known.ToArray());
                }, "参考件命名");
            }
            catch (Exception ex)
            {
                Log.Error("参考件命名失败", ex);
            }
        }

        /// <summary>
        /// 「标准件前缀」按钮的统一回调：参数是前缀在列表中的序号
        /// （命令注册成 OnPrefixCommand(0)、OnPrefixCommand(1)…）。
        /// </summary>
        public void OnPrefixCommand(string data)
        {
            try
            {
                int index;
                if (!int.TryParse(data, out index))
                {
                    return;
                }

                var prefixes = BuildPrefixButtonList();
                if (index < 0 || index >= prefixes.Count)
                {
                    return;
                }

                var prefix = prefixes[index];

                // 点到的前缀如果还没在规则列表里，顺手加进去，保证它会被 BOM 认可
                var configured = new List<string>(NamingOptionsFactory.ParsePrefixes(_settings.BomPrefixes));
                if (!configured.Contains(prefix))
                {
                    configured.Add(prefix);
                    _settings.BomPrefixes = NamingOptionsFactory.SerializePrefixes(configured);
                    _settings.Save();
                }

                ApplyPrefix(prefix, false);
            }
            catch (Exception ex)
            {
                Log.Error("前缀按钮回调失败", ex);
            }
        }

        /// <summary>标准件中文中间名按钮回调，参数是中间名在配置列表中的序号。</summary>
        public void OnMiddleNameCommand(string data)
        {
            try
            {
                int index;
                if (!int.TryParse(data, out index))
                {
                    return;
                }

                var names = BuildMiddleNameButtonList();
                if (index < 0 || index >= names.Count)
                {
                    return;
                }

                ApplyMiddleName(names[index], false);
            }
            catch (Exception ex)
            {
                Log.Error("中间名按钮回调失败", ex);
            }
        }

        public void OnMachinedLevel2Command(string data)
        {
            ApplyMachinedLevelCommand(data, BuildMachinedLevel2ButtonList(),
                MachinedSegmentKind.Material, "加工件二级字段");
        }

        public void OnMachinedLevel3Command(string data)
        {
            ApplyMachinedLevelCommand(data, BuildMachinedLevel3ButtonList(),
                MachinedSegmentKind.Name, "加工件三级字段");
        }

        private void ApplyMachinedLevelCommand(string data, IList<string> values,
            MachinedSegmentKind kind, string actionName)
        {
            try
            {
                int index;
                if (!int.TryParse(data, out index) || values == null ||
                    index < 0 || index >= values.Count)
                {
                    return;
                }

                var naming = NamingOptionsFactory.FromSettings(_settings);
                var selectedValue = values[index];
                RenameSelectedComponentFiles(delegate(string name)
                {
                    if (kind == MachinedSegmentKind.Material)
                    {
                        var knownMaterials = NamingOptionsFactory.ParsePrefixes(
                            _settings.MachinedLevel2Values);
                        return NamingOptions.SetReferenceMaterialAfterDate(
                            name, selectedValue, knownMaterials);
                    }
                    return naming.SetMachinedSegmentValue(name, kind, selectedValue);
                }, actionName + "：" + selectedValue);
            }
            catch (Exception ex)
            {
                Log.Error(actionName + "按钮回调失败", ex);
            }
        }

        /// <summary>加工件快捷按钮：把当天日期写到文件名首段。</summary>
        public void OnMachinedDateCommand()
        {
            var today = DateTime.Now.ToString("yyyyMMdd");
            RenameSelectedComponentFiles(delegate(string name)
            {
                return NamingOptions.SetLeadingDate(name, today);
            }, "写入加工件时间");
        }

        /// <summary>加工件快捷按钮：在日期段之后加入参考材料 6061。</summary>
        public void OnMachinedSeparatorCommand()
        {
            RenameSelectedComponentFiles(delegate(string name)
            {
                return NamingOptions.InsertReferenceMaterialAfterDate(name, "6061");
            }, "加入参考材料 6061");
        }

        /// <summary>一键把选中零件或子装配体文件名里的下划线全部改为短横线。</summary>
        public void OnConvertUnderscoresCommand()
        {
            RenameSelectedComponentFiles(delegate(string name)
            {
                return (name ?? string.Empty).Replace('_', '-');
            }, "下划线转换为短横线");
        }

        /// <summary>装配快捷按钮：有日期时插在日期后，否则插到文件名最前面。</summary>
        public void OnAssemblyNameCommand()
        {
            RenameSelectedComponentFiles(delegate(string name)
            {
                return NamingOptions.InsertTokenAfterDateOrStart(name, "装配");
            }, "装配命名");
        }

        /// <summary>撤回最近一次由 MechKit 快捷按钮执行的命名操作。</summary>
        public void OnMachinedUndoCommand()
        {
            var doc = SwUtils.ActiveDoc(_swApp);
            if (doc == null || string.IsNullOrEmpty(_lastNamingUndoDocument))
            {
                MessageBox.Show("当前没有可撤回的 MechKit 快捷命名。", AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var currentDocument = doc.GetPathName() ?? doc.GetTitle();
            if (!string.Equals(currentDocument, _lastNamingUndoDocument,
                    StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("最近一次快捷命名不属于当前文档，无法在这里撤回。",
                    AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                doc.EditUndo2(1);
                doc.EditRebuild3();
                int errors = 0;
                int warnings = 0;
                doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);

                var message = "已撤回最近一次 MechKit 快捷命名。";
                if (_lastNamingUndoCreatedFile)
                {
                    message += "\r\n\r\n装配体引用已经恢复；“先使之独立”生成的新文件仍保留在磁盘中，避免误删数据。";
                }
                _lastNamingUndoDocument = null;
                _lastNamingUndoCreatedFile = false;
                MessageBox.Show(message, AddinConstants.Title, MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log.Error("撤回快捷命名失败", ex);
                MessageBox.Show("撤回失败：" + ex.Message, AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public void OnPartList()
        {
            ShowPartListDialog();
        }

        public void OnBatchExport()
        {
            ShowBatchExportDialog();
        }

        public void OnPropertyTool()
        {
            ShowPropertyToolDialog();
        }

        public void OnToggleTaskPane()
        {
            ToggleTaskPane();
        }

        public void OnExportConfiguration()
        {
            try
            {
                _settings.Save();

                string targetFile;
                string message;
                var ok = _settings.ExportPortableToDesktop(out targetFile, out message);
                if (ok)
                {
                    Log.Info(message);
                    message += System.Environment.NewLine + System.Environment.NewLine +
                               "换电脑使用：把该文件复制到 MechKit.dll 所在目录，重启 SOLIDWORKS 即可。";
                }
                else
                {
                    Log.Warn(message);
                }

                MessageBox.Show(message, AddinConstants.Title,
                    MessageBoxButtons.OK,
                    ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                Log.Error("导出桌面配置失败", ex);
                MessageBox.Show("导出 MechKit 配置失败：" + ex.Message, AddinConstants.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public void OnAbout()
        {
            ShowAboutDialog();
        }

        /// <summary>启用方法返回值：1 = 可用。</summary>
        public int OnAlwaysEnable()
        {
            return 1;
        }

        #endregion

        internal static string AddinVersion
        {
            get
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                return version == null
                    ? "0.0"
                    : string.Format("{0}.{1}", version.Major, version.Minor);
            }
        }

        /// <summary>
        /// 非模态打开功能窗口。窗口之间和 SOLIDWORKS 主界面互不锁定；
        /// 同一功能重复点击时激活已有窗口，不重复创建。
        /// </summary>
        private void ShowModeless(string key, Func<Form> factory)
        {
            ShowModeless(key, factory, null);
        }

        private void ShowModeless(string key, Func<Form> factory, Action onClosed)
        {
            Form existing;
            if (_openForms.TryGetValue(key, out existing) && existing != null && !existing.IsDisposed)
            {
                if (onClosed != null)
                {
                    existing.FormClosed += delegate
                    {
                        if (!_disconnecting) onClosed();
                    };
                }
                if (existing.WindowState == FormWindowState.Minimized)
                {
                    existing.WindowState = FormWindowState.Normal;
                }
                if (!existing.Visible)
                {
                    existing.Show();
                }
                existing.BringToFront();
                existing.Activate();
                return;
            }

            var form = factory();
            _openForms[key] = form;
            form.FormClosed += delegate
            {
                Form current;
                if (_openForms.TryGetValue(key, out current) && ReferenceEquals(current, form))
                {
                    _openForms.Remove(key);
                }
                form.Dispose();
                if (onClosed != null && !_disconnecting)
                {
                    onClosed();
                }
            };

            var handle = MainWindowHandle;
            if (handle != IntPtr.Zero)
            {
                // 不把 SOLIDWORKS 主窗口当 owner：owner 会把窗口永远压在图形区之上，
                // 点图形区时沉不下去。改成“点了图形区”才主动沉到下一层。
                WindowLayout.SendToBackWhenGraphicsAreaClicked(form, handle, ActiveViewWindowHandle);
            }

            form.Show();
            form.BringToFront();
        }

        private void CloseOpenForms()
        {
            var forms = new List<Form>(_openForms.Values);
            _openForms.Clear();
            foreach (var form in forms)
            {
                try
                {
                    if (form != null && !form.IsDisposed)
                    {
                        form.Close();
                        form.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("关闭 MechKit 窗口失败：" + ex.Message);
                }
            }
        }

        /// <summary>
        /// 插件加载时注册命令组：沿用注册表里的旧布局，保留用户自定义的按钮位置。
        /// </summary>
        private void CreateCommandGroup()
        {
            // 动态命名按钮曾使用 42117 的固定七按钮布局。不同版本的 SOLIDWORKS
            // 有时不会在同一组 ID 下完整刷新选项卡，因此加载时主动清理旧组。
            foreach (var legacyId in AddinConstants.LegacyCommandGroupIds)
            {
                if (legacyId == AddinConstants.CommandGroupId)
                {
                    continue;
                }

                RemoveCommandGroup(legacyId);
            }

            var layout = BuildCommandGroup(AddinConstants.CommandGroupId, true);
            if (layout == null)
            {
                Log.Error("CommandManager 命令组创建失败；MechKit 选项卡不可用。");
                return;
            }

            ApplyLayout(layout);
            CreateCommandTabs(layout, !layout.ReusedStoredLayout);
            Log.Info(string.Format("CommandManager 命令组已创建（UserID {0}）。", layout.GroupId));
        }

        /// <summary>
        /// 一次命令组定义的注册结果：命令组对象 + 各类快捷按钮在组内的索引。
        /// 保存命名规则后会注册出新的定义（新 UserID），旧定义用完即删。
        /// </summary>
        private sealed class CommandGroupLayout
        {
            public CommandGroup Group;
            public int GroupId;
            public bool ReusedStoredLayout;
            public readonly List<int> Fixed = new List<int>();
            public readonly List<int> MachinedQuick = new List<int>();
            public readonly List<int> MachinedLevel2 = new List<int>();
            public readonly List<int> MachinedLevel3 = new List<int>();
            public readonly List<int> Prefixes = new List<int>();
            public readonly List<int> MiddleNames = new List<int>();
        }

        /// <summary>
        /// 按当前设置注册一个 CommandManager 命令组。
        /// keepStoredLayout=true 时沿用注册表里的旧布局（只在插件加载时使用）；
        /// 其余情况一律当作新定义，让 SOLIDWORKS 重新排布按钮。
        /// 返回 null 表示注册失败，调用方应保留原有命令组。
        /// </summary>
        private CommandGroupLayout BuildCommandGroup(int groupId, bool keepStoredLayout)
        {
            if (_commandManager == null)
            {
                _commandManager = _swApp.GetCommandManager(_cookie);
            }
            if (_commandManager == null)
            {
                Log.Error("无法获取 CommandManager。");
                return null;
            }

            var prefixButtons = BuildPrefixButtonList();
            var middleNameButtons = BuildMiddleNameButtonList();
            var machinedLevel2Buttons = BuildMachinedLevel2ButtonList();
            var machinedLevel3Buttons = BuildMachinedLevel3ButtonList();
            var expectedIds = new List<int>
            {
                AddinConstants.CmdGenerateBom,
                AddinConstants.CmdPartList,
                AddinConstants.CmdPartNamingRule,
                AddinConstants.CmdStandardPrefix,
                AddinConstants.CmdReferencePart,
                AddinConstants.CmdMachinedDate,
                AddinConstants.CmdConvertUnderscores,
                AddinConstants.CmdAssemblyName,
                AddinConstants.CmdBatchExport,
                AddinConstants.CmdTaskPane,
                AddinConstants.CmdExportConfiguration,
                AddinConstants.CmdSettings,
                AddinConstants.CmdAbout
            };
            for (var level2Index = 0; level2Index < machinedLevel2Buttons.Count; level2Index++)
            {
                expectedIds.Add(AddinConstants.MachinedLevel2CommandUserIdBase +
                    FindConfiguredValueIndex(_settings.MachinedLevel2Values,
                        machinedLevel2Buttons[level2Index]));
            }
            for (var level3Index = 0; level3Index < machinedLevel3Buttons.Count; level3Index++)
            {
                expectedIds.Add(AddinConstants.MachinedLevel3CommandUserIdBase +
                    FindConfiguredValueIndex(_settings.MachinedLevel3Values,
                        machinedLevel3Buttons[level3Index]));
            }
            for (var prefixIndex = 0; prefixIndex < prefixButtons.Count; prefixIndex++)
            {
                expectedIds.Add(AddinConstants.PrefixCommandUserIdBase + prefixIndex);
            }
            for (var middleIndex = 0; middleIndex < middleNameButtons.Count; middleIndex++)
            {
                expectedIds.Add(AddinConstants.MiddleNameCommandUserIdBase + middleIndex);
            }

            var layout = new CommandGroupLayout { GroupId = groupId };

            // 命令项数量/ID 变化后必须重建命令组，否则 SOLIDWORKS 会沿用旧布局。
            // 这里包含动态前缀 ID，用于清除旧版本在 Activate 之后追加命令造成的失效映射。
            var ignorePrevious = true;
            if (keepStoredLayout)
            {
                object storedIds;
                if (_commandManager.GetGroupDataFromRegistry(groupId, out storedIds))
                {
                    layout.ReusedStoredLayout = SameIds(storedIds as int[], expectedIds.ToArray());
                    ignorePrevious = !layout.ReusedStoredLayout;
                }
            }

            int errors = 0;
            var group = _commandManager.CreateCommandGroup2(
                groupId,
                AddinConstants.Title,
                AddinConstants.Description,
                string.Empty,
                -1,
                ignorePrevious,
                ref errors);

            if (group == null)
            {
                Log.Error(string.Format("创建 CommandManager 命令组失败（UserID {0}，错误码 {1}）",
                    groupId, errors));
                return null;
            }

            // IconList：每个尺寸一张横向图标条，一个命令占一格
            if (IconResources.SmallIcons.Length > 0)
            {
                group.IconList = IconResources.SmallIcons;
            }

            if (IconResources.MainIcons.Length > 0)
            {
                group.MainIconList = IconResources.MainIcons;
            }

            // 同时注册为菜单项和工具栏项：CommandManager 选项卡不可见时，用户仍可
            // 从独立 MechKit 工具栏或“工具”菜单访问全部命令。
            var tabItem = (int)(swCommandItemType_e.swToolbarItem | swCommandItemType_e.swMenuItem);

            // 图标索引对应 tools\Generate-Icons.ps1 中图标条的顺序，必须保持一致
            var indices = layout.Fixed;
            var machinedQuickIndices = layout.MachinedQuick;
            var machinedLevel2Indices = layout.MachinedLevel2;
            var machinedLevel3Indices = layout.MachinedLevel3;
            var prefixIndices = layout.Prefixes;
            var middleNameIndices = layout.MiddleNames;

            indices.Add(group.AddCommandItem2("生成BOM", -1,
                "一键汇总当前装配体，生成材料明细表 BOM（CSV，可用 Excel 打开）",
                "一键生成 BOM 表", 0, "OnGenerateBom", "OnAlwaysEnable",
                AddinConstants.CmdGenerateBom, tabItem));

            indices.Add(group.AddCommandItem2("明细汇总", -1,
                "按装配位置汇总加工件与标准件，可预览并编辑 BOM",
                "明细汇总 / BOM 预览", 1, "OnPartList", "OnAlwaysEnable",
                AddinConstants.CmdPartList, tabItem));

            indices.Add(group.AddCommandItem2("加工件命名规则", -1,
                "设置加工件命名规则：日期-材料-名称（兼容下划线读取），含图号与材料取值方式",
                "加工件命名规则设置", 2, "OnPartNamingRule", "OnAlwaysEnable",
                AddinConstants.CmdPartNamingRule, tabItem));

            indices.Add(group.AddCommandItem2("标准件前缀", -1,
                "维护标准件前缀（电机 / 电气 / 淘宝…），一键给选中的零件加前缀",
                "标准件前缀设置", 3, "OnStandardPrefix", "OnAlwaysEnable",
                AddinConstants.CmdStandardPrefix, tabItem));

            indices.Add(group.AddCommandItem2("参考件", -1,
                "给选中的零件或子装配体增加“参考-”前缀；参考件默认不进入 BOM",
                "参考件：增加参考-前缀", 10, "OnReferencePartCommand", "OnAlwaysEnable",
                AddinConstants.CmdReferencePart, tabItem));

            machinedQuickIndices.Add(group.AddCommandItem2("时间", -1,
                "把当天日期写入选中零件文件名的第一段",
                "加工件：写入当天时间", 2, "OnMachinedDateCommand", "OnAlwaysEnable",
                AddinConstants.CmdMachinedDate, tabItem));

            machinedQuickIndices.Add(group.AddCommandItem2("_ → -", -1,
                "把选中零件或子装配体文件名中的全部下划线转换为短横线",
                "一键将下划线转换为短横线", 3, "OnConvertUnderscoresCommand", "OnAlwaysEnable",
                AddinConstants.CmdConvertUnderscores, tabItem));

            machinedQuickIndices.Add(group.AddCommandItem2("装配", -1,
                "有日期时在日期后插入“装配-”，没有日期时插到文件名最前面",
                "装配：装配", 3, "OnAssemblyNameCommand", "OnAlwaysEnable",
                AddinConstants.CmdAssemblyName, tabItem));

            indices.Add(group.AddCommandItem2("批量导出", -1,
                "把工程图 / 零件 / 装配体批量导出为 PDF、DWG、STEP 等格式",
                "批量导出", 4, "OnBatchExport", "OnAlwaysEnable",
                AddinConstants.CmdBatchExport, tabItem));

            indices.Add(group.AddCommandItem2("工具箱面板", -1,
                "显示 / 隐藏 MechKit任务面板",
                "工具箱面板", 6, "OnToggleTaskPane", "OnAlwaysEnable",
                AddinConstants.CmdTaskPane, tabItem));

            indices.Add(group.AddCommandItem2("导出配置", -1,
                "把 MechKit 命名规则、预设与 BOM 设置直接导出到桌面",
                "导出配置到桌面", 4, "OnExportConfiguration", "OnAlwaysEnable",
                AddinConstants.CmdExportConfiguration, tabItem));

            indices.Add(group.AddCommandItem2("设置", -1,
                "BOM 格式、装配层级、常用目录与个人设置迁移",
                "MechKit 设置", 7, "OnSettings", "OnAlwaysEnable",
                AddinConstants.CmdSettings, tabItem));

            indices.Add(group.AddCommandItem2("关于", -1,
                "查看版本与日志位置",
                "关于 MechKit", 8, "OnAbout", "OnAlwaysEnable",
                AddinConstants.CmdAbout, tabItem));

            // 加工件二级/三级快捷按钮只注册用户在命名规则中勾选“选项卡”的项。
            // UserID 使用字段在完整列表中的位置，切换勾选项时 SOLIDWORKS 能识别
            // 命令 ID 已变化并刷新缓存，而不是沿用同数量的旧按钮文字。
            for (var i = 0; i < machinedLevel2Buttons.Count; i++)
            {
                var userId = AddinConstants.MachinedLevel2CommandUserIdBase +
                    FindConfiguredValueIndex(_settings.MachinedLevel2Values, machinedLevel2Buttons[i]);
                machinedLevel2Indices.Add(group.AddCommandItem2(machinedLevel2Buttons[i], -1,
                    "把选中加工件的材料字段设置为 " + machinedLevel2Buttons[i],
                    "加工件材料：" + machinedLevel2Buttons[i], 10 + i,
                    string.Format("OnMachinedLevel2Command({0})", i), "OnAlwaysEnable",
                    userId, tabItem));
            }
            for (var i = 0; i < machinedLevel3Buttons.Count; i++)
            {
                var userId = AddinConstants.MachinedLevel3CommandUserIdBase +
                    FindConfiguredValueIndex(_settings.MachinedLevel3Values, machinedLevel3Buttons[i]);
                machinedLevel3Indices.Add(group.AddCommandItem2(machinedLevel3Buttons[i], -1,
                    "把选中加工件的名称字段设置为 " + machinedLevel3Buttons[i],
                    "加工件名称：" + machinedLevel3Buttons[i], 10 + i,
                    string.Format("OnMachinedLevel3Command({0})", i), "OnAlwaysEnable",
                    userId, tabItem));
            }

            // 紧接「标准件前缀」（设置）按钮之后：先画一条分隔线，再排上常用前缀快捷按钮，
            // 选中零件点一下就直接加该前缀，不用打开设置窗口。
            if (prefixButtons.Count > 0)
            {
                for (var i = 0; i < prefixButtons.Count; i++)
                {
                    prefixIndices.Add(group.AddCommandItem2(prefixButtons[i], -1,
                        "给选中的零件 / 子装配体加前缀：" + prefixButtons[i],
                        prefixButtons[i], 10 + i,   // 图标条：第 10 格是分隔线，前缀从第 11 格起
                        string.Format("OnPrefixCommand({0})", i),
                        "OnAlwaysEnable",
                        AddinConstants.PrefixCommandUserIdBase + i, tabItem));
                }

                Log.Info(string.Format("前缀快捷按钮已创建：{0} 个（{1}）",
                    prefixButtons.Count, NamingOptionsFactory.SerializePrefixes(prefixButtons)));
            }

            // 第二组快捷按钮用于标准件中文中间名。先点前缀、再点中间名，
            // 即得到“前缀-中文中间名-原始名称或型号”。
            if (middleNameButtons.Count > 0)
            {
                for (var i = 0; i < middleNameButtons.Count; i++)
                {
                    middleNameIndices.Add(group.AddCommandItem2(middleNameButtons[i], -1,
                        "给选中的零件 / 子装配体设置中间名（中文描述）：" + middleNameButtons[i],
                        middleNameButtons[i], 10 + Math.Min(i, AddinConstants.MaxPrefixCommands - 1),
                        string.Format("OnMiddleNameCommand({0})", i),
                        "OnAlwaysEnable",
                        AddinConstants.MiddleNameCommandUserIdBase + i, tabItem));
                }

                Log.Info(string.Format("中间名快捷按钮已创建：{0} 个（{1}）",
                    middleNameButtons.Count, NamingOptionsFactory.SerializePrefixes(middleNameButtons)));
            }

            // 所有普通命令和动态前缀命令必须先注册，再统一激活；激活后追加会导致
            // SOLIDWORKS 的持久命令映射错位，表现为按钮可见但点击无回调。
            group.HasToolbar = true;
            group.HasMenu = true;
            // 命令组适用于零件 / 装配体 / 工程图三种文档的 CommandManager 选项卡。
            group.ShowInDocumentType = (int)swDocTemplateTypes_e.swDocTemplateTypePART
                                               | (int)swDocTemplateTypes_e.swDocTemplateTypeASSEMBLY
                                               | (int)swDocTemplateTypes_e.swDocTemplateTypeDRAWING;
            if (!group.Activate())
            {
                // 不抛异常：刷新失败时调用方要能保留原来的选项卡与命令组。
                Log.Error(string.Format("SOLIDWORKS 未能激活 MechKit 命令组（UserID {0}）。", groupId));
                return null;
            }

            // 加载时主动显示独立工具栏，避免 CommandManager 标签布局损坏时没有入口。
            // 保存命名规则触发的重建不强制显示，否则用户关闭过的工具栏会再次弹出。
            if (keepStoredLayout)
            {
                foreach (var templateType in new[]
                {
                    (int)swDocTemplateTypes_e.swDocTemplateTypePART,
                    (int)swDocTemplateTypes_e.swDocTemplateTypeASSEMBLY,
                    (int)swDocTemplateTypes_e.swDocTemplateTypeDRAWING
                })
                {
                    group.SetToolbarVisibility(true, templateType);
                }
            }

            layout.Group = group;
            return layout;
        }

        /// <summary>
        /// 在 CommandManager 里创建 MechKit 选项卡（零件 / 装配体 / 工程图各一份），
        /// 把命令按钮放进选项卡。这样无论当前打开什么文档，选项卡栏里都能找到 MechKit。
        /// </summary>
        private void CreateCommandTabs(CommandGroupLayout layout, bool forceRebuild)
        {
            if (_commandManager == null || layout == null || layout.Group == null ||
                layout.Fixed.Count == 0)
            {
                return;
            }

            var fixedIndices = layout.Fixed;
            var machinedQuickIndices = layout.MachinedQuick;
            var machinedLevel2Indices = layout.MachinedLevel2;
            var machinedLevel3Indices = layout.MachinedLevel3;
            var prefixIndices = layout.Prefixes;
            var middleNameIndices = layout.MiddleNames;

            foreach (var docType in new[]
            {
                // 注意：AddCommandTab 用的是 swDocumentTypes_e（零件 1 / 装配体 2 / 工程图 3），
                // 不是 swDocTemplateTypes_e（那是位标志：1/2/4/8）。传错会让 SOLIDWORKS 崩溃。
                (int)swDocumentTypes_e.swDocPART,
                (int)swDocumentTypes_e.swDocASSEMBLY,
                (int)swDocumentTypes_e.swDocDRAWING
            })
            {
                try
                {
                    var existingTab = _commandManager.GetCommandTab(docType, AddinConstants.Title);
                    if (existingTab != null)
                    {
                        if (!forceRebuild && CommandTabHasCommands(existingTab))
                        {
                            existingTab.Visible = true;
                            continue;
                        }

                        if (!forceRebuild)
                        {
                            Log.Warn(string.Format(
                                "检测到空的 MechKit 选项卡（文档类型 {0}），正在强制重建。", docType));
                        }
                        _commandManager.RemoveCommandTab(existingTab);
                    }

                    var tab = _commandManager.AddCommandTab(docType, AddinConstants.Title);

                    if (tab == null)
                    {
                        Log.Warn(string.Format("创建 CommandManager 选项卡失败（文档类型 {0}）。", docType));
                        continue;
                    }

                    // 左侧常用功能。
                    AddCommandTabBox(tab, new List<int> { fixedIndices[0], fixedIndices[1] },
                        (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow);

                    // 加工件固定分组：设置 + 时间 + _→- + 装配。
                    var machinedIds = new List<int>();
                    var machinedStyles = new List<int>();
                    AppendCommandTabItem(machinedIds, machinedStyles, fixedIndices[2],
                        (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow);
                    AppendCommandTabItems(machinedIds, machinedStyles, machinedQuickIndices,
                        (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextHorizontal);
                    AddMixedCommandTabBox(tab, machinedIds, machinedStyles);

                    // 勾选的二级字段排上行、三级字段排下行。
                    var machinedFieldIds = new List<int>();
                    var machinedFieldStyles = new List<int>();
                    var machinedColumns = Math.Max(
                        machinedLevel2Indices == null ? 0 : machinedLevel2Indices.Count,
                        machinedLevel3Indices == null ? 0 : machinedLevel3Indices.Count);
                    for (var column = 0; column < machinedColumns; column++)
                    {
                        if (machinedLevel2Indices != null && column < machinedLevel2Indices.Count)
                        {
                            AppendCommandTabItem(machinedFieldIds, machinedFieldStyles,
                                machinedLevel2Indices[column],
                                (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextHorizontal);
                        }
                        if (machinedLevel3Indices != null && column < machinedLevel3Indices.Count)
                        {
                            AppendCommandTabItem(machinedFieldIds, machinedFieldStyles,
                                machinedLevel3Indices[column],
                                (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextHorizontal);
                        }
                    }
                    AddMixedCommandTabBox(tab, machinedFieldIds, machinedFieldStyles);

                    // 标准件整体做成一个框（和加工件、材料并列的三个框）：
                    // 设置按钮 + 一级字段（前缀）+ 二级字段（中间名），一级排在前、
                    // 二级排在后，SOLIDWORKS 按行铺开时就是“上排一级、下排二级”。
                    var standardIds = new List<int>();
                    var standardStyles = new List<int>();
                    AppendCommandTabItem(standardIds, standardStyles, fixedIndices[3],
                        (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow);
                    AppendCommandTabItems(standardIds, standardStyles, prefixIndices,
                        (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextHorizontal);
                    AppendCommandTabItems(standardIds, standardStyles, middleNameIndices,
                        (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextHorizontal);
                    AddMixedCommandTabBox(tab, standardIds, standardStyles);

                    // 第三类“参考件”以及右侧工具功能。
                    var trailing = new List<int>();
                    for (var i = 4; i < fixedIndices.Count; i++)
                    {
                        trailing.Add(fixedIndices[i]);
                    }
                    AddCommandTabBox(tab, trailing,
                        (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow);

                    tab.Visible = true;
                }
                catch (Exception ex)
                {
                    Log.Error(string.Format("创建 CommandManager 选项卡异常（文档类型 {0}）", docType), ex);
                }
            }

            Log.Info("CommandManager 选项卡已创建。");
        }

        /// <summary>
        /// SOLIDWORKS 可能保留只有 GB 外壳、没有任何 Btn 命令的损坏选项卡。
        /// 这种选项卡能被 GetCommandTab 找到，但不会出现在 CommandManager 标签栏中。
        /// </summary>
        private static bool CommandTabHasCommands(CommandTab tab)
        {
            if (tab == null || tab.GetCommandTabBoxCount() <= 0)
            {
                return false;
            }

            var boxes = tab.CommandTabBoxes() as Array;
            if (boxes == null || boxes.Length == 0)
            {
                return false;
            }

            foreach (var value in boxes)
            {
                var box = value as CommandTabBox;
                if (box == null)
                {
                    continue;
                }

                object commandIds;
                object textStyles;
                if (box.GetCommands(out commandIds, out textStyles) > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>切换文档时检查当前文档类型的 MechKit 选项卡，缺失时自动恢复。</summary>
        private void EnsureActiveDocumentCommandTab()
        {
            if (_repairingCommandTab || _disconnecting || _commandManager == null ||
                _layout == null || _layout.Group == null)
            {
                return;
            }

            ModelDoc2 document = null;
            try
            {
                document = _swApp == null ? null : _swApp.IActiveDoc2;
                if (document == null)
                {
                    return;
                }

                var docType = document.GetType();
                var tab = _commandManager.GetCommandTab(docType, AddinConstants.Title);
                if (tab != null)
                {
                    tab.Visible = true;
                    ActivateCommandTabOnce(tab);
                    return;
                }

                _repairingCommandTab = true;
                CreateCommandTabs(_layout, false);
                tab = _commandManager.GetCommandTab(docType, AddinConstants.Title);
                if (tab != null)
                {
                    tab.Visible = true;
                    ActivateCommandTabOnce(tab);
                }
                Log.Info(string.Format("已自动恢复 MechKit 选项卡（文档类型 {0}）。", docType));
            }
            catch (Exception ex)
            {
                Log.Warn("自动恢复 MechKit 选项卡失败：" + ex.Message);
            }
            finally
            {
                _repairingCommandTab = false;
            }
        }

        /// <summary>
        /// 首次连接后只自动激活一次 MechKit。后续切换文档仅保证选项卡可见，
        /// 避免反复抢占用户正在使用的 CommandManager 选项卡。
        /// </summary>
        private void ActivateCommandTabOnce(CommandTab tab)
        {
            if (!_activateCommandTabOnNextDocument || tab == null)
            {
                return;
            }

            tab.Active = true;
            try
            {
                var document = _swApp == null ? null : _swApp.IActiveDoc2;
                if (document != null && document.Extension != null)
                {
                    // SOLIDWORKS 官方示例通过 ModelDocExtension 激活 CommandManager 标签。
                    document.Extension.ActiveCommandTab = AddinConstants.Title;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("通过文档接口激活 MechKit 选项卡失败：" + ex.Message);
            }
            _activateCommandTabOnNextDocument = false;
            Log.Info("首次加载已自动激活 MechKit 选项卡。");
        }

        /// <summary>把刚注册好的命令组设为当前布局（命令回调都按这份布局取按钮）。</summary>
        private void ApplyLayout(CommandGroupLayout layout)
        {
            _layout = layout;
            _commandGroup = layout == null ? null : layout.Group;
            _commandGroupId = layout == null ? AddinConstants.CommandGroupId : layout.GroupId;
        }

        /// <summary>删除一个命令组；旧组被删除后它的按钮定义同时失效。</summary>
        private void RemoveCommandGroup(int groupId)
        {
            if (_commandManager == null || groupId == 0)
            {
                return;
            }

            try
            {
                _commandManager.RemoveCommandGroup2(groupId, true);
            }
            catch (Exception ex)
            {
                Log.Warn(string.Format("移除 CommandManager 命令组 {0} 失败：{1}", groupId, ex.Message));
            }
        }

        /// <summary>
        /// 重建命令组时把独立工具栏的显示状态照搬过来，
        /// 免得到用户关掉的工具栏又弹出来、打开着的工具栏却消失。
        /// </summary>
        private void PreserveToolbarVisibility(CommandGroup previous, CommandGroup current)
        {
            if (previous == null || current == null)
            {
                return;
            }

            foreach (var templateType in new[]
            {
                (int)swDocTemplateTypes_e.swDocTemplateTypePART,
                (int)swDocTemplateTypes_e.swDocTemplateTypeASSEMBLY,
                (int)swDocTemplateTypes_e.swDocTemplateTypeDRAWING
            })
            {
                try
                {
                    current.SetToolbarVisibility(previous.GetToolbarVisibility(templateType), templateType);
                }
                catch (Exception ex)
                {
                    Log.Warn(string.Format("同步 MechKit 工具栏显示状态失败（文档类型 {0}）：{1}",
                        templateType, ex.Message));
                }
            }
        }

        /// <summary>
        /// 移除三种文档类型下的 MechKit 选项卡（连同里面的按钮盒）。
        /// 重建前必须先移除：选项卡里的按钮引用了旧命令组，先解除引用，
        /// SOLIDWORKS 才不会因为旧命令组被删除而丢掉整个 MechKit 标签。
        /// </summary>
        private void RemoveCommandTabs()
        {
            if (_commandManager == null)
            {
                return;
            }

            foreach (var docType in new[]
            {
                (int)swDocumentTypes_e.swDocPART,
                (int)swDocumentTypes_e.swDocASSEMBLY,
                (int)swDocumentTypes_e.swDocDRAWING
            })
            {
                // 历史上失败的刷新可能留下重名选项卡，因此循环清理干净。
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    try
                    {
                        var tab = _commandManager.GetCommandTab(docType, AddinConstants.Title);
                        if (tab == null)
                        {
                            break;
                        }

                        if (!_commandManager.RemoveCommandTab(tab))
                        {
                            Log.Warn(string.Format("移除 MechKit 选项卡失败（文档类型 {0}）。", docType));
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(string.Format("移除 MechKit 选项卡异常（文档类型 {0}）：{1}",
                            docType, ex.Message));
                        break;
                    }
                }
            }
        }

        /// <summary>校验三种文档类型的 MechKit 选项卡都存在且带有命令按钮。</summary>
        private bool CommandTabsReady()
        {
            if (_commandManager == null)
            {
                return false;
            }

            foreach (var docType in new[]
            {
                (int)swDocumentTypes_e.swDocPART,
                (int)swDocumentTypes_e.swDocASSEMBLY,
                (int)swDocumentTypes_e.swDocDRAWING
            })
            {
                try
                {
                    var tab = _commandManager.GetCommandTab(docType, AddinConstants.Title);
                    if (tab == null || !CommandTabHasCommands(tab))
                    {
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn(string.Format("检查 MechKit 选项卡失败（文档类型 {0}）：{1}",
                        docType, ex.Message));
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 取下一个命令组 UserID。SOLIDWORKS 规定按钮集合变化后必须换用新的
        /// UserID，否则会沿用上一次的布局；轮换范围见 AddinConstants。
        /// </summary>
        private int NextCommandGroupId()
        {
            var first = AddinConstants.CommandGroupId + 1;
            var last = AddinConstants.CommandGroupId + AddinConstants.CommandGroupIdCount - 1;
            for (var attempt = 0; attempt < AddinConstants.CommandGroupIdCount; attempt++)
            {
                var candidate = _nextCommandGroupId;
                _nextCommandGroupId = candidate >= last ? first : candidate + 1;
                if (candidate != _commandGroupId)
                {
                    return candidate;
                }
            }

            // 理论上不会走到这里；退回默认 ID，由 CreateCommandGroup2 自行报错。
            return AddinConstants.CommandGroupId;
        }

        /// <summary>当前文档是否正停在 MechKit 选项卡上。</summary>
        private bool IsMechKitTabActive()
        {
            try
            {
                var document = _swApp == null ? null : _swApp.IActiveDoc2;
                return document != null && document.Extension != null &&
                    string.Equals(document.Extension.ActiveCommandTab, AddinConstants.Title,
                        StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                Log.Warn("读取当前 CommandManager 选项卡失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>重新激活 MechKit 选项卡，让 SOLIDWORKS 立刻重画最新按钮。</summary>
        private void ActivateCommandTab()
        {
            try
            {
                var document = _swApp == null ? null : _swApp.IActiveDoc2;
                if (document == null || document.Extension == null)
                {
                    return;
                }

                var tab = _commandManager == null
                    ? null
                    : _commandManager.GetCommandTab(document.GetType(), AddinConstants.Title);
                if (tab != null)
                {
                    tab.Visible = true;
                    tab.Active = true;
                }

                document.Extension.ActiveCommandTab = AddinConstants.Title;
            }
            catch (Exception ex)
            {
                Log.Warn("重新激活 MechKit 选项卡失败：" + ex.Message);
            }
        }

        /// <summary>任务面板上的前缀 / 中间名快捷按钮跟随最新命名规则。</summary>
        private void RefreshTaskPaneShortcuts()
        {
            var pane = _taskPane;
            if (pane == null || pane.IsDisposed)
            {
                return;
            }

            try
            {
                if (pane.InvokeRequired)
                {
                    pane.BeginInvoke(new Action(pane.RefreshNamingShortcuts));
                }
                else
                {
                    pane.RefreshNamingShortcuts();
                }
            }
            catch (Exception ex)
            {
                Log.Warn("刷新任务面板快捷按钮失败：" + ex.Message);
            }
        }

        private void WarnRefreshFailed()
        {
            var owner = _taskPane != null && _taskPane.IsHandleCreated ? _taskPane : null;
            try
            {
                if (owner == null)
                {
                    MessageBox.Show(
                        "设置已保存，但 MechKit 选项卡刷新失败。请重启 SOLIDWORKS 让按钮与设置一致。",
                        AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show(owner,
                        "设置已保存，但 MechKit 选项卡刷新失败。请重启 SOLIDWORKS 让按钮与设置一致。",
                        AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("提示选项卡刷新失败时出错：" + ex.Message);
            }
        }


        private void AppendCommandTabItems(List<int> commandIds, List<int> textStyles,
            IList<int> itemIndices, int textStyle)
        {
            if (itemIndices == null)
            {
                return;
            }
            foreach (var itemIndex in itemIndices)
            {
                AppendCommandTabItem(commandIds, textStyles, itemIndex, textStyle);
            }
        }

        private void AppendCommandTabItem(List<int> commandIds, List<int> textStyles,
            int itemIndex, int textStyle)
        {
            commandIds.Add(_layout.Group.get_CommandID(itemIndex));
            textStyles.Add(textStyle);
        }

        private static void AddMixedCommandTabBox(CommandTab tab, List<int> commandIds,
            List<int> textStyles)
        {
            if (tab == null || commandIds == null || textStyles == null ||
                commandIds.Count == 0 || commandIds.Count != textStyles.Count)
            {
                return;
            }

            var box = tab.AddCommandTabBox();
            if (box != null)
            {
                if (!box.AddCommands(commandIds.ToArray(), textStyles.ToArray()))
                {
                    tab.RemoveCommandTabBox(box);
                    throw new InvalidOperationException("SOLIDWORKS 拒绝向 MechKit 选项卡加入命令按钮。");
                }
            }
        }

        private void AddCommandTabBox(CommandTab tab, IList<int> itemIndices, int textStyle)
        {
            if (tab == null || itemIndices == null || itemIndices.Count == 0)
            {
                return;
            }

            var commandIds = new int[itemIndices.Count];
            var textStyles = new int[itemIndices.Count];
            for (var i = 0; i < itemIndices.Count; i++)
            {
                commandIds[i] = _layout.Group.get_CommandID(itemIndices[i]);
                textStyles[i] = textStyle;
            }

            var box = tab.AddCommandTabBox();
            if (box != null)
            {
                if (!box.AddCommands(commandIds, textStyles))
                {
                    tab.RemoveCommandTabBox(box);
                    throw new InvalidOperationException("SOLIDWORKS 拒绝向 MechKit 选项卡加入命令按钮。");
                }
            }
        }

        /// <summary>
        /// 前缀快捷按钮列表 = 用户在标准件设置中维护的前缀（去重，最多 12 个）。
        /// 顺序即按钮顺序，OnPrefixCommand(序号) 用的就是这个列表。
        /// </summary>
        private List<string> BuildPrefixButtonList()
        {
            var result = new List<string>();
            var visible = new HashSet<string>(
                NamingOptionsFactory.ParsePrefixes(_settings.StandardTabPrefixes),
                StringComparer.OrdinalIgnoreCase);

            foreach (var prefix in NamingOptionsFactory.ParsePrefixes(_settings.BomPrefixes))
            {
                if (!string.IsNullOrEmpty(prefix) && visible.Contains(prefix) && !result.Contains(prefix))
                {
                    result.Add(prefix);
                }
            }

            while (result.Count > AddinConstants.MaxPrefixCommands)
            {
                result.RemoveAt(result.Count - 1);
            }

            return result;
        }

        /// <summary>中文中间名快捷按钮列表（去重，最多 12 个）。</summary>
        private List<string> BuildMiddleNameButtonList()
        {
            var result = new List<string>();
            var visible = new HashSet<string>(
                NamingOptionsFactory.ParsePrefixes(_settings.StandardTabMiddleNames),
                StringComparer.OrdinalIgnoreCase);
            foreach (var name in NamingOptionsFactory.ParsePrefixes(_settings.BomMiddleNames))
            {
                if (!string.IsNullOrEmpty(name) && visible.Contains(name) && !result.Contains(name))
                {
                    result.Add(name);
                }
            }

            while (result.Count > AddinConstants.MaxMiddleNameCommands)
            {
                result.RemoveAt(result.Count - 1);
            }

            return result;
        }

        private List<string> BuildMachinedLevel2ButtonList()
        {
            return BuildSelectedButtonList(_settings.MachinedLevel2Values,
                _settings.MachinedTabLevel2Values, AddinConstants.MaxMachinedLevel2Commands);
        }

        private List<string> BuildMachinedLevel3ButtonList()
        {
            return BuildSelectedButtonList(_settings.MachinedLevel3Values,
                _settings.MachinedTabLevel3Values, AddinConstants.MaxMachinedLevel3Commands);
        }

        private static List<string> BuildSelectedButtonList(string source, string selected,
            int maximum)
        {
            var result = new List<string>();
            var visible = new HashSet<string>(NamingOptionsFactory.ParsePrefixes(selected),
                StringComparer.OrdinalIgnoreCase);
            foreach (var value in NamingOptionsFactory.ParsePrefixes(source))
            {
                if (!string.IsNullOrEmpty(value) && visible.Contains(value) && !result.Contains(value))
                {
                    result.Add(value);
                }
            }
            while (result.Count > maximum)
            {
                result.RemoveAt(result.Count - 1);
            }
            return result;
        }

        private static int FindConfiguredValueIndex(string source, string value)
        {
            var values = NamingOptionsFactory.ParsePrefixes(source);
            for (var i = 0; i < values.Length; i++)
            {
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return 0;
        }

        private void CreateTaskPane()
        {
            if (_taskPaneView != null)
            {
                return;
            }

            try
            {
                _taskPaneView = _swApp.CreateTaskpaneView2(IconResources.TaskPaneIcon, AddinConstants.Title);
                if (_taskPaneView == null)
                {
                    Log.Warn("创建任务面板视图失败。");
                    return;
                }

                _taskPane = new TaskPaneControl(this);
                _taskPane.CreateControl();

                // 直接嵌入 WinForms 窗口句柄，无需注册 ActiveX 控件
                _taskPaneView.DisplayWindowFromHandlex64(_taskPane.Handle.ToInt64());
            }
            catch (Exception ex)
            {
                Log.Error("创建任务面板失败", ex);
                _taskPaneView = null;
            }
        }

        private void AttachEvents()
        {
            try
            {
                _events = _swApp as SldWorks;
                if (_events != null)
                {
                    _events.ActiveDocChangeNotify += OnActiveDocChangeNotify;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("订阅 SOLIDWORKS 事件失败：" + ex.Message);
            }
        }

        private void DetachEvents()
        {
            try
            {
                if (_events != null)
                {
                    _events.ActiveDocChangeNotify -= OnActiveDocChangeNotify;
                }
            }
            catch
            {
                // 断开失败不影响卸载
            }
            finally
            {
                _events = null;
            }
        }

        private int OnActiveDocChangeNotify()
        {
            try
            {
                EnsureActiveDocumentCommandTab();
                if (_taskPane != null && !_disconnecting)
                {
                    _taskPane.RefreshDocument();
                }
            }
            catch (Exception ex)
            {
                Log.Warn("刷新任务面板失败：" + ex.Message);
            }

            return 0;
        }

        private static bool SameIds(int[] stored, int[] current)
        {
            if (stored == null || stored.Length != current.Length)
            {
                return false;
            }

            for (var i = 0; i < stored.Length; i++)
            {
                if (stored[i] != current[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
