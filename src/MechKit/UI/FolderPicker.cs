using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MechKit.UI
{
    /// <summary>Windows Explorer-style folder picker with an address bar and search.</summary>
    internal static class FolderPicker
    {
        internal static string Show(IntPtr owner, string title, string initialFolder)
        {
            var dialog = (IFileDialog)new FileOpenDialog();
            IShellItem initial = null;
            IShellItem result = null;
            try
            {
                uint options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options | 0x20u | 0x40u | 0x800u); // PICKFOLDERS, FORCEFILESYSTEM, PATHMUSTEXIST
                dialog.SetTitle(title);
                dialog.SetOkButtonLabel("选择此文件夹");
                var folder = initialFolder;
                while (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                    folder = Path.GetDirectoryName(folder);
                if (!string.IsNullOrEmpty(folder))
                {
                    var iid = typeof(IShellItem).GUID;
                    if (SHCreateItemFromParsingName(folder, IntPtr.Zero, ref iid, out initial) >= 0)
                        dialog.SetFolder(initial);
                }
                var hr = dialog.Show(owner);
                if (hr == unchecked((int)0x800704C7)) return null; // User cancelled.
                Marshal.ThrowExceptionForHR(hr);
                dialog.GetResult(out result);
                IntPtr path;
                result.GetDisplayName(0x80058000, out path); // SIGDN_FILESYSPATH
                try { return Marshal.PtrToStringUni(path); }
                finally { Marshal.FreeCoTaskMem(path); }
            }
            finally
            {
                if (result != null) Marshal.ReleaseComObject(result);
                if (initial != null) Marshal.ReleaseComObject(initial);
                Marshal.ReleaseComObject(dialog);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext,
            ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialog { }

        // The declaration follows IModalWindow + IFileDialog vtable order through GetResult.
        [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr owner);
            void SetFileTypes(uint count, IntPtr filters);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem folder);
            void SetFolder(IShellItem folder);
            void GetFolder(out IShellItem folder);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr value);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint kind, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }
    }
}
