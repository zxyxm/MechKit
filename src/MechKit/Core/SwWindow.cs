using System;
using System.Windows.Forms;

namespace MechKit.Core
{
    /// <summary>把 SOLIDWORKS 主窗口句柄包装成 WinForms 需要的 owner。</summary>
    internal sealed class SwWindow : IWin32Window
    {
        private readonly IntPtr _handle;

        public SwWindow(IntPtr handle)
        {
            _handle = handle;
        }

        public IntPtr Handle
        {
            get { return _handle; }
        }
    }
}
