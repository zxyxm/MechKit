<#
.SYNOPSIS
    Tests whether the add-in COM registration can actually be activated,
    and shows which registry value breaks it when it cannot.

.DESCRIPTION
    Must be run with Windows PowerShell 5.1 (powershell.exe), NOT PowerShell 7:
    a .NET Framework COM class cannot be activated from a CoreCLR host, which
    would report a misleading 0x80070002.

    The probe writes a temporary HKEY_CURRENT_USER override for the same CLSID
    (no admin rights needed), so it can compare candidate CodeBase values, and
    removes it again afterwards.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\Probe-ComActivation.ps1
#>
[CmdletBinding()]
param(
    [string] $Guid = '{A7F3C1E2-5B4D-4E8A-9C21-3D6F0B7A5E10}',
    [string] $ClassName = 'MechKit.MechKitAddin',
    [string] $AssemblyName = 'MechKit, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'Run this with powershell.exe (Windows PowerShell 5.1), not pwsh.'
}

$clsid = [Guid]::Parse($Guid.Trim('{', '}'))
$overridePath = "HKCU:\Software\Classes\CLSID\$Guid"

function Test-Activation {
    param([string] $Label)

    try {
        $type = [Type]::GetTypeFromCLSID($clsid)
        $instance = [Activator]::CreateInstance($type)
        Write-Host ("[PASS] {0} -> {1}" -f $Label, $instance.GetType().FullName)
        if ([Runtime.InteropServices.Marshal]::IsComObject($instance)) {
            [void][Runtime.InteropServices.Marshal]::ReleaseComObject($instance)
        }
        return $true
    }
    catch {
        Write-Host ("[FAIL] {0} -> {1}" -f $Label, $_.Exception.Message.Split([char]10)[0])
        return $false
    }
}

function Set-OverrideCodeBase {
    param([string] $CodeBase)

    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("Software\Classes\CLSID\$Guid")
    $key.SetValue($null, 'MechKit.Addin', [Microsoft.Win32.RegistryValueKind]::String)

    $inproc = $key.CreateSubKey('InprocServer32')
    $inproc.SetValue($null, 'mscoree.dll', [Microsoft.Win32.RegistryValueKind]::String)
    $inproc.SetValue('ThreadingModel', 'Both', [Microsoft.Win32.RegistryValueKind]::String)
    $inproc.SetValue('Class', $ClassName, [Microsoft.Win32.RegistryValueKind]::String)
    $inproc.SetValue('Assembly', $AssemblyName, [Microsoft.Win32.RegistryValueKind]::String)
    $inproc.SetValue('RuntimeVersion', 'v4.0.30319', [Microsoft.Win32.RegistryValueKind]::String)
    $inproc.SetValue('CodeBase', $CodeBase, [Microsoft.Win32.RegistryValueKind]::String)
    $inproc.Close()
    $key.Close()
}

Write-Host ("Host      : Windows PowerShell {0}" -f $PSVersionTable.PSVersion)
Write-Host ("CLSID     : {0}" -f $Guid)
Write-Host ''

# 1. whatever the machine-wide registration says right now
$machine = Get-ItemProperty "HKLM:\SOFTWARE\Classes\CLSID\$Guid\InprocServer32" -ErrorAction SilentlyContinue
if ($machine) {
    Write-Host ("HKLM CodeBase  : {0}" -f $machine.CodeBase)
    Write-Host ("HKLM Inproc    : {0}" -f $machine.'(default)')
}

[void](Test-Activation -Label 'machine-wide registration (HKLM)')

# 2. candidate CodeBase values, compared through a per-user override
$candidates = @(
    'file:///C:/Program Files/MechKit/MechKit.dll',
    'file:///C:/Program%20Files/MechKit/MechKit.dll'
)

foreach ($candidate in $candidates) {
    $file = [uri]::UnescapeDataString($candidate)
    $path = $file -replace '^file:///', ''
    $exists = Test-Path $path

    Set-OverrideCodeBase -CodeBase $candidate
    [void](Test-Activation -Label ("override: {0} (file exists: {1})" -f $candidate, $exists))
}

# 3. clean up the override
try {
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree("Software\Classes\CLSID\$Guid", $false)
    Write-Host ''
    Write-Host 'Per-user override removed.'
}
catch {
    Write-Host ('Could not remove the override: {0}' -f $_.Exception.Message)
}
