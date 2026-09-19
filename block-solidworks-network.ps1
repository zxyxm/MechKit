<#
    禁止 SOLIDWORKS 相关程序访问互联网（含安装程序、后台服务、许可服务器）。

    用法（需要管理员权限）：
        powershell -ExecutionPolicy Bypass -File block-solidworks-network.ps1            # 应用封锁
        powershell -ExecutionPolicy Bypass -File block-solidworks-network.ps1 -Report    # 只查看当前状态
        powershell -ExecutionPolicy Bypass -File block-solidworks-network.ps1 -Remove    # 全部解除

    说明：
      * 规则按“可执行文件路径”匹配，Windows 防火墙不支持目录通配符，
        因此每次 SOLIDWORKS 修复/更新后应重新运行一次本脚本，把新增的 exe 纳入封锁。
      * 作用范围限定为 Internet，本地回环与局域网不受影响 —— 这样本地
        FlexNet 许可服务器（25734 端口）仍可正常发放许可。
      * 想连局域网一起封：把 -RemoteAddress Internet 改成 -RemoteAddress Any。
#>
[CmdletBinding()]
param(
    [switch]$Remove,
    [switch]$Report
)

$Group = 'Codex-SOLIDWORKS-NoNet'
$Log   = Join-Path $env:TEMP 'solidworks-nonet.log'

# 需要纳管的目录（SOLIDWORKS 主程序、共享组件、安装管理程序、许可服务器）
$Roots = @(
    'C:\Program Files\SOLIDWORKS Corp'
    'C:\Program Files\Common Files\SOLIDWORKS Shared'
    'C:\Program Files (x86)\Common Files\SOLIDWORKS Shared'
    'C:\Program Files (x86)\Common Files\SolidWorks Shared'
    'C:\Program Files (x86)\Common Files\SOLIDWORKS 安装管理程序'
    'C:\Windows\SolidWorks'
    'C:\SolidWorks_Flexnet_Server'
)

# 单独的安装程序（挂载的安装光盘）
$Files = @(
    'V:\setup.exe'
)
$Dirs = @(
    'V:\sldim'
)

$lines = New-Object System.Collections.Generic.List[string]
$say   = { param($m) $lines.Add([string]$m); Write-Output $m }

& $say ('=== block-solidworks-network ===')
& $say ('time     : ' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))
& $say ('elevated : ' + ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))

# ---------- 收集目标 exe ----------
$exes = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
foreach ($r in $Roots) {
    if (-not (Test-Path -LiteralPath $r)) { continue }
    Get-ChildItem -LiteralPath $r -Recurse -File -Filter '*.exe' -ErrorAction SilentlyContinue |
        ForEach-Object { [void]$exes.Add($_.FullName) }
}
foreach ($f in $Files) { if (Test-Path -LiteralPath $f) { [void]$exes.Add($f) } }
foreach ($d in $Dirs) {
    if (-not (Test-Path -LiteralPath $d)) { continue }
    Get-ChildItem -LiteralPath $d -Recurse -File -Filter '*.exe' -ErrorAction SilentlyContinue |
        ForEach-Object { [void]$exes.Add($_.FullName) }
}

$existing = @(Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue)

if ($Report) {
    & $say ('existing rules : ' + $existing.Count)
    foreach ($r in $existing) {
        $prog = ($r | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue).Program
        & $say ('  [' + $r.Direction + '/' + $r.Action + '] ' + $prog)
    }
    & $say ('target exes    : ' + $exes.Count)
    $lines | Out-File -LiteralPath $Log -Encoding UTF8
    return
}

if ($Remove) {
    if ($existing.Count -gt 0) {
        $existing | Remove-NetFirewallRule -ErrorAction SilentlyContinue
        & $say ('removed rules  : ' + $existing.Count)
    } else {
        & $say 'removed rules  : 0 (none found)'
    }
    $lines | Out-File -LiteralPath $Log -Encoding UTF8
    return
}

# ---------- 清掉旧规则，重建 ----------
if ($existing.Count -gt 0) {
    $existing | Remove-NetFirewallRule -ErrorAction SilentlyContinue
    & $say ('cleared old    : ' + $existing.Count)
}

& $say ('target exes    : ' + $exes.Count)

$made = 0
$failed = 0
foreach ($exe in ($exes | Sort-Object)) {
    $name = 'NoNet SOLIDWORKS - ' + [System.IO.Path]::GetFileName($exe)
    if ($name.Length -gt 200) { $name = $name.Substring(0, 200) }
    foreach ($dir in @('Outbound', 'Inbound')) {
        try {
            New-NetFirewallRule -DisplayName $name -Group $Group -Direction $dir -Action Block `
                -Program $exe -RemoteAddress Internet -Profile Any -Enabled True `
                -Description ('Blocked for SOLIDWORKS: ' + $exe) -ErrorAction Stop | Out-Null
            $made++
        } catch {
            $failed++
            & $say ('  FAIL ' + $dir + ' ' + $exe + ' :: ' + $_.Exception.Message)
        }
    }
}

& $say ('rules created  : ' + $made)
& $say ('rules failed   : ' + $failed)
& $say ('rules total    : ' + @(Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue).Count)
& $say ('scope          : RemoteAddress=Internet (局域网/回环放行)')
$lines | Out-File -LiteralPath $Log -Encoding UTF8
