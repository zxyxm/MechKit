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
        private ITaskpaneView _taskPaneView;
        private TaskPaneControl _taskPane;
        private AddinSettings _settings;
        private SldWorks _events;
        private bool _disconnecting;

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
                    // RuntimeOnly=false：连同注册表中的布局数据一起清除，
                    // 下次加载时命令组会按当前代码重新创建。
                    _commandManager.RemoveCommandGroup2(AddinConstants.CommandGroupId, false);
                    Marshal.ReleaseComObject(_commandManager);
                    _commandManager = null;
                }

                _commandGroup = null;

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

        public void ShowBatchExportDialog()
        {
            using (var form = new BatchExportForm(this))
            {
                ShowDialog(form);
            }
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
                var path = PartListService.ExportCsv(rows, folder, title);
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
                ReadCustomProperties = _settings.PartListReadProperties,
                AssemblyLevel = _settings.BomAssemblyLevel,
                StandardNameField = _settings.BomStandardNameField,
                StandardMaterialField = _settings.BomStandardMaterialField,
                StandardProcessField = _settings.BomStandardProcessField,
                StandardRemarkField = _settings.BomStandardRemarkField,
                MachinedNameField = _settings.BomMachinedNameField,
                MachinedMaterialField = _settings.BomMachinedMaterialField,
                MachinedProcessField = _settings.BomMachinedProcessField,
                MachinedRemarkField = _settings.BomMachinedRemarkField
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
                using (var form = new PartListForm(this))
                {
                    ShowDialog(form);
                }
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
            using (var form = new SettingsForm(this))
            {
                ShowDialog(form);
            }
        }

        /// <summary>打开命名规则设置：0 = 加工件栏，1 = 标准件栏。</summary>
        public void ShowNamingRuleDialog(int tabIndex)
        {
            using (var form = new NamingRuleForm(this, tabIndex))
            {
                ShowDialog(form);
            }
        }

        /// <summary>
        /// 给选中的组件加/去命名前缀。命名规则：前缀_日期_材料_名称（下划线分段），
        /// 只有符合该规则的零件才会进入 BOM。
        /// </summary>
        public void ApplyPrefix(string prefix, bool remove)
        {
            try
            {
                var doc = SwUtils.ActiveDoc(_swApp);
                if (doc == null)
                {
                    MessageBox.Show("请先打开装配体，并在树里或图形区选中零件 / 子装配体。",
                        AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var selection = doc.SelectionManager as SelectionMgr;
                if (selection == null)
                {
                    return;
                }

                var count = selection.GetSelectedObjectCount2(-1);
                if (count <= 0)
                {
                    MessageBox.Show("请先选中要处理的零件或子装配体。", AddinConstants.Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 设置页保存的前缀都视为“已知前缀”。从面板切换前缀时替换旧值，
                // 不叠加成“气动_电机_”。
                var knownList = new List<string>(NamingOptionsFactory.ParsePrefixes(_settings.BomPrefixes));
                var known = knownList.ToArray();
                var changed = 0;

                for (var i = 1; i <= count; i++)
                {
                    Component2 component;
                    try
                    {
                        component = selection.GetSelectedObject6(i, -1) as Component2;
                    }
                    catch
                    {
                        continue;
                    }

                    if (component == null)
                    {
                        continue;
                    }

                    var name = component.Name2;
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    var updated = remove ? StripPrefix(name, known) : AddPrefix(name, prefix, known);
                    if (string.Equals(updated, name, StringComparison.Ordinal) || string.IsNullOrEmpty(updated))
                    {
                        continue;
                    }

                    try
                    {
                        component.Name2 = updated;
                        changed++;
                        Log.Info(string.Format("重命名组件：{0} → {1}", name, updated));
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(string.Format("重命名组件失败：{0} - {1}", name, ex.Message));
                    }
                }

                MessageBox.Show(string.Format("已{0}前缀：{1} 个组件。{2}",
                        remove ? "去掉" : "添加", changed,
                        changed == 0 ? "\r\n（这些名称本来就不需要改动）" : string.Empty),
                    AddinConstants.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log.Error("加前缀失败", ex);
            }
        }

        private static string AddPrefix(string name, string prefix, string[] known)
        {
            var value = (prefix ?? string.Empty).Trim().TrimEnd('_', '*', '＊');
            if (value.Length == 0)
            {
                return name;
            }

            return value + "_" + StripPrefix(name, known);
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

                var withSeparator = prefix.Trim() + "_";
                if (name.StartsWith(withSeparator, StringComparison.OrdinalIgnoreCase))
                {
                    return name.Substring(withSeparator.Length);
                }
            }

            return name;
        }

        public void ShowPropertyToolDialog()
        {
            using (var form = new PropertyToolForm(this))
            {
                ShowDialog(form);
            }
        }

        public void ShowAboutDialog()
        {
            using (var form = new AboutForm(this))
            {
                ShowDialog(form);
            }
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

        private void ShowDialog(Form form)
        {
            var handle = MainWindowHandle;
            if (handle != IntPtr.Zero)
            {
                form.ShowDialog(new SwWindow(handle));
            }
            else
            {
                form.ShowDialog();
            }
        }

        private void CreateCommandGroup()
        {
            _commandManager = _swApp.GetCommandManager(_cookie);
            if (_commandManager == null)
            {
                Log.Error("无法获取 CommandManager。");
                return;
            }

            var prefixButtons = BuildPrefixButtonList();
            var expectedIds = new List<int>(AddinConstants.CommandIds);
            for (var prefixIndex = 0; prefixIndex < prefixButtons.Count; prefixIndex++)
            {
                expectedIds.Add(AddinConstants.PrefixCommandUserIdBase + prefixIndex);
            }

            // 命令项数量/ID 变化后必须重建命令组，否则 SOLIDWORKS 会沿用旧布局。
            // 这里包含动态前缀 ID，用于清除旧版本在 Activate 之后追加命令造成的失效映射。
            var ignorePrevious = false;
            object storedIds;
            if (_commandManager.GetGroupDataFromRegistry(AddinConstants.CommandGroupId, out storedIds))
            {
                ignorePrevious = !SameIds(storedIds as int[], expectedIds.ToArray());
            }

            int errors = 0;
            _commandGroup = _commandManager.CreateCommandGroup2(
                AddinConstants.CommandGroupId,
                AddinConstants.Title,
                AddinConstants.Description,
                string.Empty,
                -1,
                ignorePrevious,
                ref errors);

            if (_commandGroup == null)
            {
                Log.Error(string.Format("创建 CommandManager 命令组失败，错误码 {0}", errors));
                return;
            }

            // IconList：每个尺寸一张横向图标条，一个命令占一格
            if (IconResources.SmallIcons.Length > 0)
            {
                _commandGroup.IconList = IconResources.SmallIcons;
            }

            if (IconResources.MainIcons.Length > 0)
            {
                _commandGroup.MainIconList = IconResources.MainIcons;
            }

            var menuAndToolbar = (int)(swCommandItemType_e.swMenuItem | swCommandItemType_e.swToolbarItem);

            // 图标索引对应 tools\Generate-Icons.ps1 中图标条的顺序，必须保持一致
            var indices = new List<int>();
            var prefixIndices = new List<int>();

            indices.Add(_commandGroup.AddCommandItem2("生成BOM", -1,
                "一键汇总当前装配体，生成材料明细表 BOM（CSV，可用 Excel 打开）",
                "一键生成 BOM 表", 0, "OnGenerateBom", "OnAlwaysEnable",
                AddinConstants.CmdGenerateBom, menuAndToolbar));

            indices.Add(_commandGroup.AddCommandItem2("明细汇总", -1,
                "按装配位置汇总加工件与标准件，可预览并编辑 BOM",
                "明细汇总 / BOM 预览", 1, "OnPartList", "OnAlwaysEnable",
                AddinConstants.CmdPartList, menuAndToolbar));

            indices.Add(_commandGroup.AddCommandItem2("加工件命名规则", -1,
                "设置加工件命名规则：日期_材料_名称（下划线分段），含图号与材料取值方式",
                "加工件命名规则设置", 2, "OnPartNamingRule", "OnAlwaysEnable",
                AddinConstants.CmdPartNamingRule, menuAndToolbar));

            indices.Add(_commandGroup.AddCommandItem2("标准件前缀", -1,
                "维护标准件前缀（电机 / 电气 / 淘宝…），一键给选中的零件加前缀",
                "标准件前缀设置", 3, "OnStandardPrefix", "OnAlwaysEnable",
                AddinConstants.CmdStandardPrefix, menuAndToolbar));

            indices.Add(_commandGroup.AddCommandItem2("批量导出", -1,
                "把工程图 / 零件 / 装配体批量导出为 PDF、DWG、STEP 等格式",
                "批量导出", 4, "OnBatchExport", "OnAlwaysEnable",
                AddinConstants.CmdBatchExport, menuAndToolbar));

            indices.Add(_commandGroup.AddCommandItem2("工具箱面板", -1,
                "显示 / 隐藏 MechKit任务面板",
                "工具箱面板", 6, "OnToggleTaskPane", "OnAlwaysEnable",
                AddinConstants.CmdTaskPane, menuAndToolbar));

            indices.Add(_commandGroup.AddCommandItem2("设置", -1,
                "BOM 格式、装配层级、常用目录与个人设置迁移",
                "MechKit 设置", 7, "OnSettings", "OnAlwaysEnable",
                AddinConstants.CmdSettings, menuAndToolbar));

            indices.Add(_commandGroup.AddCommandItem2("关于", -1,
                "查看版本与日志位置",
                "关于 MechKit", 8, "OnAbout", "OnAlwaysEnable",
                AddinConstants.CmdAbout, menuAndToolbar));

            // 紧接「标准件前缀」（设置）按钮之后：先画一条分隔线，再排上常用前缀快捷按钮，
            // 选中零件点一下就直接加该前缀，不用打开设置窗口。
            if (prefixButtons.Count > 0)
            {
                _commandGroup.AddSpacer2(-1, AddinConstants.PrefixSpacerUserId);

                for (var i = 0; i < prefixButtons.Count; i++)
                {
                    prefixIndices.Add(_commandGroup.AddCommandItem2(prefixButtons[i], -1,
                        "给选中的零件 / 子装配体加前缀：" + prefixButtons[i],
                        prefixButtons[i], 10 + i,   // 图标条：第 10 格是分隔线，前缀从第 11 格起
                        string.Format("OnPrefixCommand({0})", i),
                        "OnAlwaysEnable",
                        AddinConstants.PrefixCommandUserIdBase + i, menuAndToolbar));
                }

                Log.Info(string.Format("前缀快捷按钮已创建：{0} 个（{1}）",
                    prefixButtons.Count, NamingOptionsFactory.SerializePrefixes(prefixButtons)));
            }

            // 所有普通命令和动态前缀命令必须先注册，再统一激活；激活后追加会导致
            // SOLIDWORKS 的持久命令映射错位，表现为按钮可见但点击无回调。
            _commandGroup.HasToolbar = true;
            _commandGroup.HasMenu = true;
            // 菜单在零件 / 装配体 / 工程图里都要出现
            _commandGroup.ShowInDocumentType = (int)swDocTemplateTypes_e.swDocTemplateTypePART
                                               | (int)swDocTemplateTypes_e.swDocTemplateTypeASSEMBLY
                                               | (int)swDocTemplateTypes_e.swDocTemplateTypeDRAWING;
            _commandGroup.Activate();

            // 明确重建 MechKit 选项卡，保证动态前缀按钮出现在“标准件前缀设置”之后。
            // 仅依赖 CommandGroup 自动布局时，SOLIDWORKS 会沿用旧的五按钮布局，
            // 新增的电机/电气/淘宝等命令虽然已注册，却不会显示在选项卡中。
            var tabIndices = new List<int>();
            for (var i = 0; i < indices.Count; i++)
            {
                tabIndices.Add(indices[i]);
                if (i == 3)
                {
                    tabIndices.AddRange(prefixIndices);
                }
            }
            CreateCommandTabs(tabIndices);

            // 让工具栏在安装后立刻可见（装的当天就能在工具栏上看到按钮）
            try
            {
                var toolbarId = _commandGroup.ToolbarId;
                if (toolbarId >= 0)
                {
                    _commandGroup.SetToolbarVisibility(true, toolbarId);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("显示工具栏失败（可从「视图 > 工具栏」手动打开）：" + ex.Message);
            }

            Log.Info("CommandManager 命令组已创建。");
        }

        /// <summary>
        /// 在 CommandManager 里创建 MechKit 选项卡（零件 / 装配体 / 工程图各一份），
        /// 把命令按钮放进选项卡。这样无论当前打开什么文档，选项卡栏里都能找到 MechKit。
        /// </summary>
        private void CreateCommandTabs(IList<int> itemIndices)
        {
            if (_commandManager == null || _commandGroup == null || itemIndices == null || itemIndices.Count == 0)
            {
                return;
            }

            var commandIds = new int[itemIndices.Count];
            for (var i = 0; i < itemIndices.Count; i++)
            {
                commandIds[i] = _commandGroup.get_CommandID(itemIndices[i]);
            }

            var textStyles = new int[commandIds.Length];
            for (var i = 0; i < textStyles.Length; i++)
            {
                textStyles[i] = (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow;
            }

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
                        _commandManager.RemoveCommandTab(existingTab);
                    }

                    var tab = _commandManager.AddCommandTab(docType, AddinConstants.Title);

                    if (tab == null)
                    {
                        Log.Warn(string.Format("创建 CommandManager 选项卡失败（文档类型 {0}）。", docType));
                        continue;
                    }

                    var box = tab.AddCommandTabBox();
                    if (box != null)
                    {
                        box.AddCommands(commandIds, textStyles);
                    }

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
        /// 前缀快捷按钮列表 = 用户在标准件设置中维护的前缀（去重，最多 12 个）。
        /// 顺序即按钮顺序，OnPrefixCommand(序号) 用的就是这个列表。
        /// </summary>
        private List<string> BuildPrefixButtonList()
        {
            var result = new List<string>();

            foreach (var prefix in NamingOptionsFactory.ParsePrefixes(_settings.BomPrefixes))
            {
                if (!string.IsNullOrEmpty(prefix) && !result.Contains(prefix))
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
