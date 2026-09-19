using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

[assembly: AssemblyTitle("MechKit")]
[assembly: AssemblyDescription("SOLIDWORKS 工具箱插件")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("MechKit")]
[assembly: AssemblyCopyright("")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// 程序集默认对 COM 不可见，仅插件主类显式暴露
[assembly: ComVisible(false)]
[assembly: Guid("2f0a4d18-6c33-4f27-9a55-71b6c0d5e9a1")]

[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]

// 允许离线测试宿主直接使用插件内部的窗口与逻辑，无需 SOLIDWORKS。
[assembly: InternalsVisibleTo("MechKitHarness")]
