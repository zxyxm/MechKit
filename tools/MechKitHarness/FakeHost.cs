using System;
using MechKit;
using MechKit.Core;
using SolidWorks.Interop.sldworks;

namespace MechKit.Harness
{
    /// <summary>
    /// 假宿主：不连接 SOLIDWORKS，只提供窗口需要的设置与空实现，
    /// 用来在不启动 SOLIDWORKS 的情况下预览 / 调试各个对话框和任务面板。
    /// </summary>
    internal sealed class FakeHost : IAddinHost
    {
        public FakeHost()
        {
            Settings = AddinSettings.Load();
        }

        /// <summary>没有 SOLIDWORKS 上下文，接口调用会走"未打开文档"的分支。</summary>
        public ISldWorks SwApp
        {
            get { return null; }
        }

        public AddinSettings Settings { get; private set; }

        public IntPtr MainWindowHandle
        {
            get { return IntPtr.Zero; }
        }

        public void GenerateBom()
        {
            Log.Info("[harness] 生成BOM 被点击（离线预览模式，不做实际操作）");
        }

        public void ShowBatchExportDialog()
        {
            Log.Info("[harness] 批量导出 被点击");
        }

        public void ShowPartListDialog()
        {
            Log.Info("[harness] 明细汇总 被点击");
        }

        public void ShowSettingsDialog()
        {
            Log.Info("[harness] 设置 被点击");
        }

        public void ShowNamingRuleDialog(int tabIndex)
        {
            Log.Info("[harness] 命名规则设置 被点击（栏 " + tabIndex + "）");
        }

        public void ApplyPrefix(string prefix, bool remove)
        {
            Log.Info(string.Format("[harness] {0}前缀：{1}（离线预览，不做实际操作）",
                remove ? "去掉" : "添加", prefix));
        }

        public void ApplyMiddleName(string middleName, bool remove)
        {
            Log.Info(string.Format("[harness] {0}中间名：{1}（离线预览，不做实际操作）",
                remove ? "去掉" : "设置", middleName));
        }

        public void ShowPropertyToolDialog()
        {
            Log.Info("[harness] 属性工具 被点击");
        }

        public void ShowAboutDialog()
        {
            Log.Info("[harness] 关于 被点击");
        }

        public void ToggleTaskPane()
        {
            Log.Info("[harness] 工具箱面板 被点击");
        }
    }
}
