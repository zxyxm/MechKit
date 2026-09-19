<#
.SYNOPSIS
    Enables CLR assembly-binding logging for the current user.

.DESCRIPTION
    When SOLIDWORKS refuses to load a managed add-in, the reason is almost
    always an assembly-binding failure inside the SOLIDWORKS process. This
    turns on Fusion logging (HKCU only, no admin needed) so the failure shows
    up as an HTML file.

    Usage:
      powershell -ExecutionPolicy Bypass -File tools\Enable-FusionLog.ps1
      powershell -ExecutionPolicy Bypass -File tools\Enable-FusionLog.ps1 -Disable
#>
[CmdletBinding()]
param(
    [string] $LogPath,
    [switch] $Disable,
    [switch] $Status
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $LogPath) {
    $LogPath = Join-Path $env:LOCALAPPDATA 'MechKit\fusion'
}

$key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Fusion')

if ($Status) {
    $current = Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Fusion' -ErrorAction SilentlyContinue
    if ($current) { $current | Format-List } else { Write-Host 'Fusion logging is not configured.' }
    exit 0
}

try {
    if ($Disable) {
        $key.SetValue('EnableLog', 0, [Microsoft.Win32.RegistryValueKind]::DWord)
        $key.SetValue('ForceLog', 0, [Microsoft.Win32.RegistryValueKind]::DWord)
        $key.SetValue('LogFailures', 0, [Microsoft.Win32.RegistryValueKind]::DWord)
        Write-Host 'Fusion logging disabled.'
    }
    else {
        if (-not (Test-Path $LogPath)) { New-Item -ItemType Directory -Path $LogPath -Force | Out-Null }
        $key.SetValue('EnableLog', 1, [Microsoft.Win32.RegistryValueKind]::DWord)
        $key.SetValue('ForceLog', 1, [Microsoft.Win32.RegistryValueKind]::DWord)
        $key.SetValue('LogFailures', 1, [Microsoft.Win32.RegistryValueKind]::DWord)
        $key.SetValue('LogResourceBinds', 1, [Microsoft.Win32.RegistryValueKind]::DWord)
        $key.SetValue('LogPath', $LogPath, [Microsoft.Win32.RegistryValueKind]::String)
        Write-Host "Fusion logging enabled. Logs: $LogPath"
    }
}
finally {
    $key.Close()
}
