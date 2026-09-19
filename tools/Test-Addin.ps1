<#
.SYNOPSIS
    Verifies the MechKit add-in without (or with) starting SOLIDWORKS.

.DESCRIPTION
    Checks:
      1. the assembly exports the COM-visible MechKit.MechKitAddin class
      2. the COM registration can actually be activated (ProgID -> CoCreateInstance)
      3. the object implements ISwAddin
      4. the SOLIDWORKS add-in registry entries exist
      5. with -LaunchSolidWorks: start SOLIDWORKS, load the add-in and confirm
         that ConnectToSW ran (log file + ISldWorks::GetAddInObject)

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-Addin.ps1
    powershell -ExecutionPolicy Bypass -File tools\Test-Addin.ps1 -LaunchSolidWorks
#>
[CmdletBinding()]
param(
    [string] $DllPath,

    [string] $SolidWorksPath,

    [switch] $LaunchSolidWorks,

    [int] $TimeoutSeconds = 180
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$failures = New-Object System.Collections.Generic.List[string]

function Write-Result {
    param([string] $Name, [bool] $Ok, [string] $Detail)

    $mark = if ($Ok) { 'PASS' } else { 'FAIL' }
    Write-Host ("[{0}] {1}{2}" -f $mark, $Name, ($(if ($Detail) { " - $Detail" } else { '' })))
    if (-not $Ok) { $script:failures.Add($Name) }
}

function Write-Note {
    param([string] $Text, [string] $Detail)

    Write-Host ("[note] {0}{1}" -f $Text, ($(if ($Detail) { " - $Detail" } else { '' })))
}

function Resolve-AddinDll {
    param([string] $Candidate)

    if ($Candidate) { return (Resolve-Path -LiteralPath $Candidate).Path }

    foreach ($configuration in @('Release', 'Debug')) {
        $path = Join-Path $repoRoot "src\MechKit\bin\$configuration\MechKit.dll"
        if (Test-Path $path) { return (Resolve-Path -LiteralPath $path).Path }
    }

    throw 'MechKit.dll not found. Build first.'
}

function Resolve-SolidWorksExe {
    param([string] $Override)

    if ($Override) {
        if (Test-Path $Override) { return (Resolve-Path -LiteralPath $Override).Path }
        throw "SOLIDWORKS executable not found: $Override"
    }

    foreach ($root in @('HKLM:\SOFTWARE\SolidWorks', 'HKLM:\SOFTWARE\WOW6432Node\SolidWorks')) {
        if (-not (Test-Path $root)) { continue }

        foreach ($key in (Get-ChildItem $root -ErrorAction SilentlyContinue | Sort-Object PSChildName -Descending)) {
            if ($key.PSChildName -notmatch '^SOLIDWORKS \d{4}$') { continue }
            $setupKey = Join-Path $key.PSPath 'Setup'
            if (-not (Test-Path $setupKey)) { continue }

            $setup = Get-ItemProperty -Path $setupKey -ErrorAction SilentlyContinue
            if ($setup -and ($setup.PSObject.Properties.Name -contains 'SolidWorks Folder')) {
                $exe = Join-Path $setup.'SolidWorks Folder'.TrimEnd('\') 'SLDWORKS.exe'
                if (Test-Path $exe) { return $exe }
            }
        }
    }

    foreach ($candidate in @(
        'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS (2)\SLDWORKS.exe',
        'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe'
    )) {
        if (Test-Path $candidate) { return $candidate }
    }

    throw 'SLDWORKS.exe not found; pass -SolidWorksPath.'
}

$dll = Resolve-AddinDll -Candidate $DllPath
Write-Host "Assembly: $dll"
Write-Host ''

# 1. assembly + COM metadata -------------------------------------------------
$assembly = [System.Reflection.Assembly]::LoadFrom($dll)
$type = $assembly.GetType('MechKit.MechKitAddin', $false)
Write-Result 'Assembly exports MechKit.MechKitAddin' ($null -ne $type) $dll
if (-not $type) { exit 1 }

$comVisible = $type.IsPublic -and ($type.GetCustomAttributes(
    [System.Runtime.InteropServices.ComVisibleAttribute], $false).Count -gt 0)
Write-Result 'Add-in class is COM visible' $comVisible

$interfaceImplemented = $null -ne $type.GetInterface('SolidWorks.Interop.swpublished.ISwAddin')
Write-Result 'Implements ISwAddin' $interfaceImplemented

$guid = '{' + $type.GUID.ToString().ToUpperInvariant() + '}'
$constants = $assembly.GetType('MechKit.AddinConstants', $false)
$progId = $constants.GetField('ProgId').GetValue($null)
$title = $constants.GetField('Title').GetValue($null)

# 2. registry ----------------------------------------------------------------
$machineKey = "HKLM:\SOFTWARE\SolidWorks\AddIns\$guid"
$userKey = "HKCU:\SOFTWARE\SolidWorks\AddIns\$guid"
Write-Result 'SOLIDWORKS AddIns entry (HKLM, required)' (Test-Path $machineKey) $machineKey
if (Test-Path $userKey) {
    Write-Note 'HKCU AddIns entry exists (development leftover, not required)' $userKey
}

$startupKey = "HKCU:\Software\SolidWorks\AddInsStartup\$guid"
$machineStartup = "HKLM:\SOFTWARE\SolidWorks\AddInsStartup\$guid"
Write-Result 'AddInsStartup (load at launch)' ((Test-Path $startupKey) -or (Test-Path $machineStartup)) `
    "$machineStartup | $startupKey"

$machineClsid = "HKLM:\SOFTWARE\Classes\CLSID\$guid\InprocServer32"
$userClsid = "HKCU:\Software\Classes\CLSID\$guid\InprocServer32"
Write-Result 'COM InprocServer32 (HKLM)' (Test-Path $machineClsid) $machineClsid
if (Test-Path $userClsid) {
    Write-Note 'HKCU CLSID exists (would shadow the machine-wide CodeBase)' $userClsid
}

$dotNetCategory = "HKLM:\SOFTWARE\Classes\CLSID\$guid\Implemented Categories\{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}"
Write-Result 'COM .NET category (regasm parity)' (Test-Path $dotNetCategory) $dotNetCategory

# 3. COM activation ----------------------------------------------------------
$progIdType = [Type]::GetTypeFromProgID($progId)
Write-Result 'ProgID resolves to a COM type' ($null -ne $progIdType) $progId

if ($progIdType) {
    try {
        $instance = [Activator]::CreateInstance($progIdType)
        $ok = $null -ne $instance
        Write-Result 'COM object can be created' $ok $instance.GetType().FullName

        if ($ok -and [System.Runtime.InteropServices.Marshal]::IsComObject($instance)) {
            [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($instance)
        }
    }
    catch {
        Write-Result 'COM object can be created' $false $_.Exception.Message
    }
}

# 4. optional end-to-end check with SOLIDWORKS -------------------------------
if ($LaunchSolidWorks) {
    $exe = Resolve-SolidWorksExe -Override $SolidWorksPath
    $logDirectory = Join-Path $env:LOCALAPPDATA 'MechKit\logs'
    $stamp = Get-Date

    $running = Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host '[SKIP] SOLIDWORKS is already running; using the existing session.'
    }
    else {
        Write-Host "Starting $exe ..."
        Start-Process -FilePath $exe -WindowStyle Minimized | Out-Null
    }

    $swApp = $null
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline -and -not $swApp) {
        Start-Sleep -Seconds 5
        try {
            # Early in startup SOLIDWORKS publishes a bootstrap object that does
            # not answer API calls yet, so only accept an object that replies.
            $candidate = [System.Runtime.InteropServices.Marshal]::GetActiveObject('SldWorks.Application')
            [void]$candidate.RevisionNumber()
            $swApp = $candidate
        }
        catch {
            $swApp = $null
        }
    }

    if (-not $swApp) {
        Write-Result 'Connected to the SOLIDWORKS API' $false "timed out after $TimeoutSeconds s"
    }
    else {
        $revision = 'unknown'
        try { $revision = $swApp.RevisionNumber() } catch { }
        Write-Result 'Connected to the SOLIDWORKS API' $true ("revision " + $revision)

        try {
            $addinObject = $swApp.GetAddInObject($guid)
            Write-Result 'SOLIDWORKS can load the add-in (GetAddInObject)' ($null -ne $addinObject) $guid
            if ($addinObject) {
                [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($addinObject)
            }
        }
        catch {
            Write-Result 'SOLIDWORKS can load the add-in (GetAddInObject)' $false $_.Exception.Message
        }
    }

    # the add-in writes a log line on ConnectToSW
    $logFile = Join-Path $logDirectory ("MechKit-{0:yyyyMMdd}.log" -f (Get-Date))
    $foundConnect = $false
    if (Test-Path $logFile) {
        $foundConnect = $null -ne (Select-String -Path $logFile -Pattern '[connect]' -SimpleMatch -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTime -ge $stamp } | Select-Object -First 1)
    }

    Write-Result 'Add-in wrote ConnectToSW log entry' $foundConnect $logFile
}

Write-Host ''
if ($failures.Count -eq 0) {
    Write-Host 'All checks passed.'
}
else {
    Write-Host ("Failed checks: {0}" -f ($failures -join ', '))
    exit 1
}
