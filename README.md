# MechKit（MechKit）

面向 SOLIDWORKS 2024 的插件，为「加工件 BOM / 明细汇总」这类日常工作服务：

- **BOM 预览/明细汇总**：窗口打开后自动汇总，可选择总装、子装配体或单个零件作为 BOM 范围，直接编辑**名称、材料、工艺**，按命名规则反推并重命名零件文件，也可导出 CSV。
- **任务面板**：显示当前文档的图号、材料，点一下即可复制；零件属性可直接编辑并写回。
- **批量导出**：工程图导出 PDF / DWG / DXF，模型导出 STEP / IGES / STL。
- **属性批量写入**：把同一组自定义属性写入整个文件夹的文档。
- **配置快速导出**：点击 CommandManager 的「导出配置」，直接在桌面生成 `MechKit-settings.ini`，便于换电脑迁移。

---

## 1. 明细汇总怎么用

点击 CommandManager 的「MechKit」选项卡 → **明细汇总**（或菜单「工具 → 明细汇总」）。

若只需要某个部件的 BOM：保持预览窗口打开，在 SOLIDWORKS 装配树或图形区选中
零件/子装配体，再点预览窗口里的「选择部件」。表格会重新统计该部件范围，
此时「导出部件 BOM」只导出当前表格；点「全部装配体」可恢复总装范围。

也可以点击「读取子装配体」，插件会递归读取当前总装下的部件装配体并显示在
「部件装配体」下拉框中。选择任意部件后会立即按该部件重新统计 BOM；嵌套部件
以“上级部件 > 下级部件”的形式显示，便于直接选择，无需先在装配树中定位。

子装配体按下面的规则决定要不要继续往下读取（跟随「按命名规则过滤」开关，关闭后恢复“全部往下读”的旧行为）：

- **标准件前缀开头**的子装配体（`淘宝-`、`代理-`、`整机-`…）是整机外购件：BOM 里按**一条标准件**整体计入，不再读取它内部的零件。识别用的前缀就是「命名规则 → 标准件一级字段」，例如想把“整机”也当外购件，把 `整机` 加进一级字段即可。
- **按加工件命名**的子装配体（日期开头，例如 `20260425-装配-水箱总装`）才继续往下读取，逐层统计内部的零件与子装配体。
- 其它**不符合命名规则**的子装配体（例如 `气密性二代`、`P80-02-01-01-0治疗通道组件`）不展开：内部零件不参与统计，但装配体本身会排到表格**最后并标红**，提醒补命名规则。

不符合「加工件 / 标准件」命名的单个组件同样不会凭空消失：它们排在表格最后并**整行标红**，属性列显示 `未匹配`；参考件（`参考-` 开头）属于明确规则，照旧不进 BOM、也不标红。类型筛选里新增「只看未匹配」，可以先只看这些行再回装配树改名。

1. 窗口打开后会自动预览当前装配体或零件的 BOM；也可点「刷新预览」。
2. 或选「文件夹」，批量汇总一批零件。
3. 双击黄色单元格可编辑名称、材料、工艺和备注；装配说明列为只读，由设置页选择的文件名分段或自定义属性读取。
4. 双击表格任意一行会打开该零件的图：优先同目录同名的工程图（`.slddrw`），该零件没有工程图时改为打开零件 / 装配体模型；已打开的文档会自动切换到它。单元格编辑用单击选中后直接输入或按 `F2`。
5. 点「按规则重命名文件」会从表格中的名称、材料、工艺反推文件名：加工件保留日期和尾部变更段，标准件按“工艺-名称-型号”组装；随后通过 SOLIDWORKS 重命名接口更新零件文件和当前装配引用。此操作不会写入自定义属性，备注不参与文件名。

表格最左侧是「导出」勾选列：勾选需要的行后点「导出选中件…」，插件会把勾选的零件 / 装配体直接带进批量导出窗口（有同名工程图的自动换成工程图），在那里选格式（PDF / DWG / DXF / STEP / IGES / STL）和输出目录即可开始。底部「全选」可以一次勾选当前表格里的所有行。

表格左上角（行头与列头交汇处）是「表头设置」入口：点一下直接打开「设置 → BOM 格式」，改表头名称、字段来源和列顺序，不用再从菜单绕。

「三维名称」列就是零件 / 装配体的文件名，可以直接改：**双击名字文字**进入编辑，提交后会问一次是否把文件重命名（走 SOLIDWORKS 的重命名接口，装配引用同步更新，不会断装配关系）；选择“否”就只保留表格里的名字，稍后点「按规则重命名文件」统一执行。

**双击名字后面的空白区域**（或其它列）会打开这一行的**三维模型**；三维文件打不开时才退回打开工程图。需要在表格里直接改名字、又不想动文件时，用「按规则重命名文件」按钮即可。

「二维工程图」列显示同目录同名的工程图文件名，并影响整行底色：

- **有二维工程图** → 整行浅绿；
- **加工件没有二维工程图** → 整行浅红（提醒补图）；
- 标准件 / 参考件没有工程图时不标色（标准件可以有追加工图纸，有图就会标绿）。

另外，在表格里选中一行（或多行）后，直接点 MechKit 选项卡上的快捷按钮（前缀 / 中间名 / 加工件材料…）就会作用于选中的这些零件，不必再回装配树里重新选一遍；关掉 BOM 窗口后按钮恢复成“用 SOLIDWORKS 里的选择”。

默认输出列：`序号 | 位置 | 完整名称 | 属性 | 零件名称/标准件名称 | 材料/型号 | 工艺/渠道 | 表面处理 | 数量 | 安装说明 | 备注`。“完整名称”保留未拆分的零部件名称（不含扩展名和装配实例序号）。所有表头都可在「设置 → BOM 格式」直接改名；通过底部“移动表头”选择列并点击“左移 / 右移”即可调整顺序。BOM 预览和 CSV 导出使用相同顺序、表头和内容。加工件的“表面处理”“安装说明”会自动寻找命名规则中的同名分段；未配置相应分段时留空。

「设置 → BOM 格式」中的字段下拉菜单优先跟随命名规则：加工件只显示当前实际存在的分段，并标出已勾选“并入 BOM”的段；标准件固定为三级“前缀-中间名-型号”，例如 `淘宝-按钮-S22`。第三级型号会整体保留，型号自身包含短横线时不会被拆成额外级别。开启“使用 SOLIDWORKS 属性表字段”后，下拉菜单末尾才会追加名称、材料、工艺、备注和装配说明等属性来源；关闭后仅按文件命名规则解析。

类型分三类：

| 类型 | 判定方式 |
| --- | --- |
| 标准件 | 路径含 `\Toolbox\` / `\SOLIDWORKS Data\`；或文件名含 `GB/T`、`DIN`、螺栓、轴承等关键词 |
| 外购件 | 文件名含厂商关键词（嘉立创、米思米/MISUMI、怡合达、THK、上银、SMC、亚德客、欧姆龙、基恩士…）；或自定义属性「来源/类型」写了外购 |
| 加工件 | 以上都不是 |

勾选「只列加工件」后，汇总统计只算加工件的种类数与总数量。

## 2. 命名规则（重点）

### 2.1 图号

默认取**完整文件名（不含扩展名）**。图号来源 / 截断规则不再是界面上的常驻选项，设置值保留在配置里，需要切换时告诉我（或直接改 `%LOCALAPPDATA%\MechKit\settings.ini` 的 `PartNumberSource` / `PartNumberCutRule` / `PartNumberPattern`）：

| 选项 | 说明 |
| --- | --- |
| 文件名 | 直接用文件名 |
| 自定义属性优先 | 先读「图号 / 零件号 / 零件代号 / 代号 / PartNumber」，读不到再退回文件名 |
| 仅自定义属性 | 只认自定义属性 |

「文件名截断」还可以取第一个空格前 / 下划线前 / 短横线前，或自定义正则（正则捕获组 1 优先）。

### 2.2 名称与材料按段解析

新名称统一使用 `-` 分隔；读取和 BOM 识别同时兼容旧的 `_` 与 `-`：

```
20260908-6061-扫码枪安装板.sldprt
   ↓       ↓        ↓
 （日期） 材料=6061  名称=扫码枪安装板
```

- **时间段**固定为第 1 段，用来判定加工件，不能移动或删除
- 默认仅保留五段：**日期、材料/工艺、零件名称、版本号、安装说明**
- 版本号和安装说明默认并入 BOM 零件名称；其他**自定义段**仍可通过 `＋ 增加段` 添加
- 新增的**自定义段**可直接输入名称，例如“供应商”“表面处理”“项目号”，保存后会保留
- 除时间段外，可用 `↑ / ↓` 调整顺序，也可删除；材料和名称会按调整后的段位置解析
- 勾选某段右侧的“并入 BOM 名称”，该段会和零件名称一起显示在 BOM；例如勾选第 3～5 段后，`板1-A-左装` 会作为 BOM 零件名

只有文件名里真的分段时才会生效；`拨线板.sldprt` 这类名字会原样作为名称，材料留空。

加工件二级字段（材料）使用**一张合并表**维护，一行就是一条材料段配置：

| 列 | 作用 |
| --- | --- |
| 材料段（文件名取值） | 文件名里材料段实际写的字，例如 `6061`；留空时按“材料”列取值 |
| 材料 / 工艺 / 表面处理 | 这一行在 BOM 里对应的材料、工艺、表面处理；某一段留空表示该列不填 |
| 选项卡 | 勾选后该材料段**立即**显示在标准件快捷区上排（按钮顺序 = 表格行顺序） |

默认 `6061` 解析为 `6061 / cnc / 本色氧化`，`5052` 解析为 `5052 / 钣金 / 白色细砂纹烤漆`；`淘宝` 这类“材料留空、只写工艺”的映射把材料段填 `淘宝`、工艺填 `追加工` 即可。行尾 `×` 删除该行，底部「增加材料段」输入后点「＋ 增加」新增一行。保存后仍按 `材料段=材料|工艺|表面处理` 与材料段列表分别存储，旧配置会自动合并到同一张表里。

命名规则、BOM 表头设置和生成结果使用同一套字段语义：调整命名段顺序或自定义段名称后，BOM 设置中的加工件字段来源会按“名称 / 材料 / 版本号 / 安装说明”等含义自动跟随，不依赖固定第几段；表头名称、字段来源和左右顺序会直接用于 BOM 预览与 CSV 导出。

插件命令只显示在 SOLIDWORKS CommandManager 的 `MechKit` 选项卡中，不创建独立工具栏；选项卡布局会在正常退出后保留，并在切换文档时自动检查和恢复。

命名规则页可维护快捷字段：加工件二级字段（材料）在「材料段二级解析与快捷按钮」的一张表里维护，勾选“选项卡”的字段会出现在标准件快捷区上排；标准件一级字段为渠道/前缀，二级字段为中文名称，两排按钮“上一级在上、下一级在下”，不同级别不会混排。点击快捷按钮会按当前命名段顺序更新对应字段。

加工件三级字段（零件名称）的按钮配置仍保留在设置里（`MachinedLevel3Values` / `MachinedTabLevel3Values`），只是不再占用设置页界面。

保存命名规则时会立即重建 `MechKit` 选项卡上的快捷按钮：新增字段、勾选或取消勾选“选项卡”都会当场生效，不需要重启 SOLIDWORKS；任务面板上的前缀 / 中间名按钮也同步刷新。SOLIDWORKS 要求“命令按钮集合变化时换用新的命令组 UserID”，插件每次刷新都会用新 ID 重建命令组与选项卡，再删除旧命令组，因此不会出现旧按钮残留或整个 MechKit 标签消失。

所有独立窗口共用响应式窗口规则：根据当前屏幕工作区限制初始及最小尺寸，兼容字体/DPI 缩放，恢复上次位置时自动拉回可见区域；内容较长的设置页使用滚动区域，底部操作按钮保持独立。

### 2.3 模板占位符会被忽略

中文模板会把未填写的标题栏属性留成 `“图样名称”`、`“图样代号”`、`材质 <未指定>`，
这些值会被当作空，不会污染汇总结果。

### 2.4 标准件前缀

「标准件（前缀开头）」设置页只负责维护前缀：输入名称后点「增加」，或点击当前前缀上的 `×` 删除；前缀按空格分隔，例如 `电机 淘宝 气动`。
保存后的前缀与中间名会显示为 SOLIDWORKS MechKit 选项卡及任务面板上的快捷按钮；默认中间名包括接近开关、直线导轨、电机和丝杆，其中直线导轨默认绑定代理前缀。先选中零件，再点击按钮即可直接改名。

标准件一级字段、二级字段的每一行后面都可以写**备注**（例如给「传动」写上“直线导轨和直线模组和滚珠丝杆等”）。备注只用于记录、方便自己辨认，不参与命名和 BOM 判断，会随设置一起保存并包含在便携配置里。

“中间名 → 前缀绑定”规则本身仍在配置里生效（例如点“直线导轨”会自动带上“代理”前缀），只是不再占用设置页界面；想调整绑定关系时告诉我，或直接改 `%LOCALAPPDATA%\MechKit\settings.ini` 的 `StandardPrefixBindingEnabled` / `StandardPrefixBindings`。
快捷按钮现在通过 SOLIDWORKS 的文档重命名接口修改零件文件名，而不只是修改组件实例显示名。每次操作可选择直接修改原零件，或先使当前组件独立再修改；后者会在原目录新建一个零件文件，并只替换当前选中的实例。完成后会明确提示新建文件。
加工件快捷区另提供「时间」「6061-」「_→-」「撤回」和「装配」按钮：「时间」写入当天日期首段，已有合法日期前缀时更新为当天日期；「6061-」在合法日期首段后插入参考材料，例如 `20260919-安装板` 改为 `20260919-6061-安装板`，已存在时不会重复添加；「_→-」把选中零件或子装配体文件名中的全部下划线转换为短横线；「撤回」撤销最近一次 MechKit 快捷命名；「装配」给选中组件文件名增加 `装配-` 前缀，已有时不重复。
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
powershell -ExecutionPolicy Bypass -File tools\Test-Naming.ps1
powershell -ExecutionPolicy Bypass -File tools\Test-BomIntegration.ps1

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
DocInspector.exe "D:\项目\20260908-6061-扫码枪安装板.sldprt"

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
