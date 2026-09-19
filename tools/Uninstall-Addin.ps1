<#
.SYNOPSIS
    Removes the MechKit add-in registration (machine-wide and per-user).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Uninstall-Addin.ps1 -Elevate
    powershell -ExecutionPolicy Bypass -File tools\Uninstall-Addin.ps1 -Elevate -RemoveFiles
#>
[CmdletBinding()]
param(
    [string] $DllPath,

    [string] $InstallDirectory = (Join-Path $env:ProgramFiles 'MechKit'),

    # Also delete the copied add-in files
    [switch] $RemoveFiles,

    [switch] $Elevate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

function Test-IsElevated {
    return ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsElevated)) {
    if (-not $Elevate) {
        throw 'Administrator rights are required to remove the machine-wide registration. Re-run with -Elevate.'
    }

    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -Elevate' -f $PSCommandPath
    if ($InstallDirectory) { $arguments += ' -InstallDirectory "{0}"' -f $InstallDirectory }
    if ($DllPath) { $arguments += ' -DllPath "{0}"' -f $DllPath }
    if ($RemoveFiles) { $arguments += ' -RemoveFiles' }

    Write-Host 'Requesting administrator rights (a UAC prompt will appear)...'
    $process = Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Verb RunAs -Wait -PassThru
    exit $process.ExitCode
}

function Resolve-MetadataSource {
    param([string] $Candidate)

    if ($Candidate -and (Test-Path $Candidate)) { return (Resolve-Path -LiteralPath $Candidate).Path }

    $installed = Join-Path $InstallDirectory 'MechKit.dll'
    if (Test-Path $installed) { return $installed }

    foreach ($configuration in @('Release', 'Debug')) {
        $path = Join-Path $repoRoot "src\MechKit\bin\$configuration\MechKit.dll"
        if (Test-Path $path) { return $path }
    }

    throw 'MechKit.dll not found; pass -DllPath to unregister a specific build.'
}

$source = Resolve-MetadataSource -Candidate $DllPath
$assembly = [System.Reflection.Assembly]::LoadFrom($source)
$type = $assembly.GetType('MechKit.MechKitAddin', $false)
if (-not $type) { throw "Type MechKit.MechKitAddin not found in $source" }

$constants = $assembly.GetType('MechKit.AddinConstants', $false)
$guid = '{' + $type.GUID.ToString().ToUpperInvariant() + '}'
$progId = $constants.GetField('ProgId').GetValue($null)

function Remove-Tree {
    param([Microsoft.Win32.RegistryKey] $Root, [string] $Path)

    try {
        $Root.DeleteSubKeyTree($Path, $false)
        Write-Host "Removed $($Root.Name)\$Path"
    }
    catch [System.ArgumentException] {
        Write-Host "Not present: $($Root.Name)\$Path"
    }
}

foreach ($root in @([Microsoft.Win32.Registry]::LocalMachine, [Microsoft.Win32.Registry]::CurrentUser)) {
    Remove-Tree -Root $root -Path "SOFTWARE\Classes\CLSID\$guid"
    Remove-Tree -Root $root -Path "SOFTWARE\Classes\$progId"
    Remove-Tree -Root $root -Path "SOFTWARE\SolidWorks\AddIns\$guid"
    Remove-Tree -Root $root -Path "SOFTWARE\SolidWorks\AddInsStartup\$guid"
}

if ($RemoveFiles -and (Test-Path $InstallDirectory)) {
    # only delete the folder when it looks like our own install directory
    $marker = Join-Path $InstallDirectory 'MechKit.dll'
    if (Test-Path $marker) {
        Remove-Item -LiteralPath $InstallDirectory -Recurse -Force
        Write-Host "Removed $InstallDirectory"
    }
    else {
        Write-Host "Skipped $InstallDirectory (MechKit.dll not found inside)"
    }
}

Write-Host ''
Write-Host 'Unregistered. Restart SOLIDWORKS to drop the add-in.'
