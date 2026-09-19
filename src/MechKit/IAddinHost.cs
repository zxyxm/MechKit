using System;
using SolidWorks.Interop.sldworks;
using MechKit.Core;

namespace MechKit
{
    /// <summary>
    /// 插件向 UI 暴露的接口。UI 只依赖它，便于单独测试和替换。
    /// </summary>
    internal interface IAddinHost
    {
        ISldWorks SwApp { get; }

        AddinSettings Settings { get; }

        /// <summary>SOLIDWORKS 主窗口句柄，用作对话框 owner；不可用时为 IntPtr.Zero。</summary>
        IntPtr MainWindowHandle { get; }

        void ShowBatchExportDialog();

        void GenerateBom();

        void ShowPartListDialog();

        void ShowSettingsDialog();

        /// <summary>打开命名规则设置（tabIndex 0 = 加工件，1 = 标准件）。</summary>
        void ShowNamingRuleDialog(int tabIndex);

        /// <summary>给选中的零件/子装配体加前缀（remove=true 时去掉已知前缀）。</summary>
        void ApplyPrefix(string prefix, bool remove);

        void ShowPropertyToolDialog();

        void ShowAboutDialog();

        void ToggleTaskPane();
    }
}
