MechKit —— 交付包说明
================================================

本目录包含两部分：

  1、安装包\      给使用者的成品，双击「安装与卸载.exe」即可
  2、源码\        完整 C# 源码，可用 Visual Studio / MSBuild 重新编译


一、只想用插件
------------------------------------------------
进入「1、安装包」文件夹 → 双击「安装与卸载.exe」→ UAC 点「是」→ 点「安装」。
安装完重启 SOLIDWORKS，就会出现「MechKit」选项卡和工具栏。
详细说明见「1、安装包\安装说明.txt」。


二、想改代码 / 重新编译
------------------------------------------------
1. 环境要求
   · Visual Studio 2022（含 .NET 桌面开发）或 MSBuild
   · SOLIDWORKS 2024（用来引用 SolidWorks.Interop.*.dll 和调试）
   · .NET Framework 4.8

2. 编译插件
   powershell -ExecutionPolicy Bypass -File build.ps1
   产物：src\MechKit\bin\Release\MechKit.dll

3. 重新生成交付包（安装包 + 源码）
   powershell -ExecutionPolicy Bypass -File build-dist.ps1

4. 做一次完整验证
   powershell -ExecutionPolicy Bypass -File tools\Test-Naming.ps1     图号/材料规则单元测试
   powershell -ExecutionPolicy Bypass -File tools\Test-Addin.ps1      注册与 COM 检查


三、源码结构
------------------------------------------------
src\MechKit\
  MechKitAddin.cs    插件入口（ISwAddin、命令回调、注册表注册）
  AddinConstants.cs    GUID / 命令 ID / 名称
  IAddinHost.cs        UI 与插件之间的接口
  Core\
    PartNaming.cs      图号 / 名称 / 材料解析规则（含占位符过滤）
    VendorKeywords.cs  外购件、标准件关键词
    AddinSettings.cs   配置持久化（%LOCALAPPDATA%\MechKit\settings.ini）
    Log.cs / AppPaths.cs / IconResources.cs / SwUtils.cs / SwWindow.cs
  Features\
    PartListService.cs 装配体遍历、加工件数量统计、CSV 导出、属性写回
    PropertyService.cs 自定义属性读写（含配置特定属性）
    ExportService.cs   批量导出 PDF / DWG / DXF / STEP / IGES / STL
  UI\
    TaskPaneControl.cs 任务面板（图号 / 材料 / 属性编辑）
    PartListForm.cs    明细汇总窗口
    BatchExportForm.cs / PropertyToolForm.cs / AboutForm.cs / Theme.cs
  Resources\icons\     命令图标（20/32/40/64/96/128 六种尺寸）

installer\
  MechKitSetup\      「安装与卸载.exe」的源码（WinForms + requireAdministrator）
  dist\                安装说明、config.ini 等打包素材

tools\
  Install-Addin.ps1 / Uninstall-Addin.ps1   命令行安装 / 卸载
  Test-Addin.ps1 / Test-Naming.ps1          验证脚本
  Generate-Icons.ps1                        重新生成图标
  Enable-FusionLog.ps1                      排查插件加载失败用
  DocInspector\                             命令行诊断工具（读真实图纸验证规则）


四、两个技术要点（改代码前值得看一眼）
------------------------------------------------
1. 插件必须注册到 HKLM\SOFTWARE\SolidWorks\AddIns\{GUID}。
   SOLIDWORKS 不认当前用户（HKCU）的注册，所以安装必须提权。

2. 部分系统上 HKLM\SOFTWARE\Classes 这个键本身不可写，只能创建它的子键。
   因此注册代码必须直接 CreateSubKey 完整路径，不要先打开
   "SOFTWARE\Classes" 再往下建，否则会「访问被拒绝」。

更完整的说明见「源码\README.md」。
