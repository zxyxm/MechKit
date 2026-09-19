# MechKit（MechKit）

面向 SOLIDWORKS 2024 的插件，为「加工件 BOM / 明细汇总」这类日常工作服务：

- **BOM 预览/明细汇总**：窗口打开后自动汇总，可直接编辑**名称、材料、工艺**并写回零件自定义属性，也可导出 CSV。
- **任务面板**：显示当前文档的图号、材料，点一下即可复制；零件属性可直接编辑并写回。
- **批量导出**：工程图导出 PDF / DWG / DXF，模型导出 STEP / IGES / STL。
- **属性批量写入**：把同一组自定义属性写入整个文件夹的文档。

---

## 1. 明细汇总怎么用

点击 CommandManager 的「MechKit」选项卡 → **明细汇总**（或菜单「工具 → 明细汇总」）。

1. 窗口打开后会自动预览当前装配体或零件的 BOM；也可点「刷新预览」。
2. 或选「文件夹」，批量汇总一批零件。
3. 双击黄色单元格可编辑名称、材料、工艺和备注；点「应用 BOM 修改」只写入零件属性并保存。
4. 点「写入并重命名」会同时更新当前装配体中的组件实例名，但不会改名或移动零件文件，装配引用和配合关系保持不变。

输出列：`序号 | 图号 | 名称 | 材料 | 工艺 | 数量 | 类型 | 配置 | 文件名`

类型分三类：

| 类型 | 判定方式 |
| --- | --- |
| 标准件 | 路径含 `\Toolbox\` / `\SOLIDWORKS Data\`；或文件名含 `GB/T`、`DIN`、螺栓、轴承等关键词 |
| 外购件 | 文件名含厂商关键词（嘉立创、米思米/MISUMI、怡合达、THK、上银、SMC、亚德客、欧姆龙、基恩士…）；或自定义属性「来源/类型」写了外购 |
| 加工件 | 以上都不是 |

勾选「只列加工件」后，汇总统计只算加工件的种类数与总数量。

## 2. 命名规则（重点）

### 2.1 图号

默认取**完整文件名（不含扩展名）**。可在「图号来源」里改成：

| 选项 | 说明 |
| --- | --- |
| 文件名 | 直接用文件名 |
| 自定义属性优先 | 先读「图号 / 零件号 / 零件代号 / 代号 / PartNumber」，读不到再退回文件名 |
| 仅自定义属性 | 只认自定义属性 |

「文件名截断」还可以取第一个空格前 / 下划线前 / 短横线前，或自定义正则（正则捕获组 1 优先）。

### 2.2 名称与材料按段解析

加工件规则默认使用分隔符 `_`，并按界面中从上到下的段顺序解析：

```
20260908_6061_扫码枪安装板.sldprt
   ↓       ↓        ↓
 （日期） 材料=6061  名称=扫码枪安装板
```

- **时间段**固定为第 1 段，用来判定加工件，不能移动或删除
- **材料段、零件名称段、扩展序号段、自定义段**可通过 `＋ 增加段` 添加
- 新增的**自定义段**可直接输入名称，例如“供应商”“表面处理”“项目号”，保存后会保留
- 除时间段外，可用 `↑ / ↓` 调整顺序，也可删除；材料和名称会按调整后的段位置解析

只有文件名里真的分段时才会生效；`拨线板.sldprt` 这类名字会原样作为名称，材料留空。

### 2.3 模板占位符会被忽略

中文模板会把未填写的标题栏属性留成 `“图样名称”`、`“图样代号”`、`材质 <未指定>`，
这些值会被当作空，不会污染汇总结果。

### 2.4 标准件前缀

「标准件（前缀开头）」设置页只负责维护前缀：输入名称后点「增加」，或点击当前前缀上的 `×` 删除；前缀按空格分隔，例如 `电机 淘宝 气动`。
保存后的前缀会显示为 SOLIDWORKS MechKit 选项卡及任务面板上的快捷按钮；先选中零件，再点击前缀按钮即可直接改名。
BOM 收录范围等通用选项统一在「个人配置」中设置，不在前缀页重复显示。

### 换电脑迁移 MechKit 配置

在「设置 → 个人配置」中点击「导出 MechKit 配置…」，会生成
`MechKit-settings.ini`。把该文件复制到另一台电脑的 MechKit 安装根目录
（与 `MechKit.dll` 同目录，默认 `C:\MechKit`），重启 SOLIDWORKS 后自动生效。

便携配置包含命名规则、标准件前缀/中间名、绑定规则、材料工艺预设和 BOM
字段映射；不会携带窗口位置、输出目录及 SOLIDWORKS 本机目录，避免换机后路径失效。

## 3. 安装 / 卸载

### 3.1 给最终用户：安装包（与嘉立创 Ican 相同的结构）

```powershell
powershell -ExecutionPolicy Bypass -File build-dist.ps1
```

生成 `dist\MechKit工具箱 V0.1\`：

```
MechKit工具箱 V0.1\
├─ 安装与卸载.exe          双击 → UAC → 点「安装」即可
├─ MechKit.dll           插件本体
├─ SolidWorks.Interop.sldworks.dll / swconst / swpublished   互操作程序集（随插件分发）
├─ config.ini
├─ 安装说明.txt
└─ 诊断工具\DocInspector.exe
```

`安装与卸载.exe` 支持静默调用，便于批量部署：

```powershell
安装与卸载.exe /install   /log C:\temp\setup.log
安装与卸载.exe /uninstall /removefiles /log C:\temp\setup.log
```

### 3.2 给开发/维护：PowerShell 脚本

```powershell
# 构建（自动探测 SOLIDWORKS 安装目录）
powershell -ExecutionPolicy Bypass -File build.ps1

# 安装：复制文件 + 机器级注册（会自动弹 UAC 提权）
powershell -ExecutionPolicy Bypass -File tools\Install-Addin.ps1 -Elevate

# 卸载（-RemoveFiles 连安装目录一起删）
powershell -ExecutionPolicy Bypass -File tools\Uninstall-Addin.ps1 -Elevate -RemoveFiles

# 验证（不启动 SOLIDWORKS）
powershell -ExecutionPolicy Bypass -File tools\Test-Addin.ps1

# 验证（启动 SOLIDWORKS 并确认插件被加载）
powershell -ExecutionPolicy Bypass -File tools\Test-Addin.ps1 -LaunchSolidWorks
```

### 安装做了什么（与嘉立创 Ican 工具箱同一套思路）

1. 把 `MechKit.dll` 和三个互操作程序集复制到 `C:\MechKit\`
   （和 Ican 一样：插件自带互操作程序集，不依赖宿主目录探测）。
2. 机器级注册 COM，布局与 `regasm /codebase`、`SolidWorksAddinInstaller.exe` 完全一致：

   ```
   HKLM\SOFTWARE\Classes\CLSID\{GUID}
     (Default)                      = MechKit.Addin
     \InprocServer32                = mscoree.dll, ThreadingModel=Both, Class, Assembly,
                                      RuntimeVersion=v4.0.30319, CodeBase
     \InprocServer32\0.1.0.0        = 同上（regasm 的版本子键）
     \ProgId                        = MechKit.Addin
     \Implemented Categories\{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}   (.NET 分类)
   HKLM\SOFTWARE\Classes\MechKit.Addin  +  \CLSID = {GUID}
   ```

3. 写入 SOLIDWORKS 插件清单与开机加载：

   ```
   HKLM\SOFTWARE\SolidWorks\AddIns\{GUID}          Title / Description
   HKLM + HKCU \SOFTWARE\SolidWorks\AddInsStartup\{GUID} = 1
   ```

4. 清掉开发期残留的 HKCU 注册，避免旧 CodeBase 指向 `bin\Release` 造成版本错乱。

安装完重启 SOLIDWORKS，就会出现：

- CommandManager 新增 **MechKit** 选项卡；
- 工具栏出现同名工具条（插件在加载时会调用 `SetToolbarVisibility`，装完即可见）；
- 「工具」菜单底部出现同样的命令；
- 「工具 → 插件」列表中已勾选 *Start Up*，之后随 SOLIDWORKS 自动加载。

> 如果工具条被手动关掉了，可从「视图 → 工具栏」重新勾选；想默认不弹出，
> 删除 `MechKitAddin.CreateCommandGroup()` 里的 `SetToolbarVisibility` 调用即可。

> **为什么必须要管理员权限？**
> SOLIDWORKS 只从 `HKEY_LOCAL_MACHINE\SOFTWARE\SolidWorks\AddIns\{GUID}` 读取插件列表
> （官方文档 *Distribute SOLIDWORKS Add-in* 的要求）。只写当前用户配置单元（HKCU）
> 时，SOLIDWORKS 启动时会把这个键删掉，插件不会出现在「工具 → 插件」里。
>
> 不带 `-Machine` 时脚本只做当前用户注册，用于开发期快速验证 COM 部分。
>
> **常见坑：`HKLM\SOFTWARE\Classes` 这个键本身可能不可写**（部分加固过的系统只允许
> 创建它的子键）。因此安装程序始终按完整路径 `CreateSubKey`，
> 不会先打开 `SOFTWARE\Classes` 再往下建——否则会出现"访问被拒绝"。

## 4. 构建环境

| 组件 | 本机版本 |
| --- | --- |
| SOLIDWORKS | 2024（32.5.0），安装目录由注册表 `SOLIDWORKS <年份>\Setup\SolidWorks Folder` 自动探测 |
| 目标框架 | .NET Framework 4.8 / x64 |
| 编译器 | MSBuild（Visual Studio 2022） |

构建产物：

- `src\MechKit\bin\Release\MechKit.dll` —— 插件本体（同目录附带互操作程序集）
- `tools\DocInspector\bin\Release\DocInspector.exe` —— 命令行诊断工具

## 5. 命令行诊断工具

不改动、不保存任何文档，用于核对规则和排查问题：

```powershell
# 看一个装配体的加工件明细（表 + 可选 CSV）
DocInspector.exe "D:\项目\总装.SLDASM" --csv "$env:TEMP\bom"

# 看单个零件解析出的图号 / 名称 / 材料
DocInspector.exe "D:\项目\20260908_6061_扫码枪安装板.sldprt"

# 看文件夹里前 6 个文件的材料与自定义属性
DocInspector.exe "D:\项目\零件" --limit 6

# 关闭「按段解析名称/材料」
DocInspector.exe "D:\项目\总装.SLDASM" --segments off
```

## 6. 目录结构

```
MechKitAddin/
├─ build.ps1                     构建（自动探测 SOLIDWORKS 路径）
├─ src/MechKit/
│  ├─ MechKitAddin.cs          插件入口：ISwAddin、命令回调、注册表注册
│  ├─ AddinConstants.cs          GUID、命令 ID、名称
│  ├─ IAddinHost.cs              UI 与插件之间的接口
│  ├─ Core/                      路径、日志、配置、图号规则、外购件关键词
│  ├─ Features/                  明细汇总、属性读写、批量导出
│  ├─ UI/                        任务面板与各对话框
│  └─ Resources/icons/           命令图标（按 20/32/40/64/96/128 生成）
└─ tools/
   ├─ Generate-Icons.ps1         重新生成图标
   ├─ Install-Addin.ps1          注册插件
   ├─ Uninstall-Addin.ps1        注销插件
   ├─ Test-Addin.ps1             注册与加载验证
   ├─ Test-Naming.ps1            图号/材料规则离线测试
   ├─ testdata/                  测试用例（中文放在 JSON 里，脚本保持纯 ASCII）
   └─ DocInspector/              命令行诊断工具
```

### 离线测试选项卡 UI

不启动 SOLIDWORKS，直接打开与 CommandManager 同尺寸、同按钮逻辑的测试界面：

```powershell
tools\MechKitHarness\bin\Release\MechKitHarness.exe --tab
```

也可以自动渲染一张 1050×150 的客户端预览图，用于布局对比：

```powershell
tools\MechKitHarness\bin\Release\MechKitHarness.exe --tab-image=C:\Temp\MechKit-tab.png
```

## 7. 常见问题

**插件没出现在「工具 → 插件」里**：确认用 `-Machine` 注册过，并重启 SOLIDWORKS。
可用 `tools\Test-Addin.ps1` 逐项检查注册表与 COM 激活。

**日志在哪**：`%LOCALAPPDATA%\MechKit\logs\MechKit-YYYYMMDD.log`。
每次连接会写 `[connect]`，卸载写 `[disconnect]`。

**想调整外购件识别范围**：编辑 `src\MechKit\Core\VendorKeywords.cs` 的
`Purchased` / `Standard` 关键词数组，重新构建即可。

**数量不对**：压缩的组件默认不计数；如需要，取消勾选「忽略压缩的组件」。
同一零件不同配置会分行统计。

## 8. 脚本编写约定

`tools\*.ps1` 与 `build.ps1` **必须保持纯 ASCII**：
Windows PowerShell 5.1 会用系统 ANSI 代码页读取无 BOM 的脚本，
脚本里直接写中文会破坏解析（中文放 JSON、C# 或本文档里）。
C# 文件的中文依赖 `<CodePage>65001</CodePage>` 编译选项。
