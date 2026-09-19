<#
.SYNOPSIS
    Installs the MechKit add-in the same way the Ican toolbox does:
    copy the files to a stable folder, then register machine-wide.

.DESCRIPTION
    Mirrors the SOLIDWORKS SDK registration layout (what regasm /codebase and
    SolidWorksAddinInstaller.exe produce):

      HKLM\SOFTWARE\Classes\CLSID\{guid}
        (Default)                      = ProgId
        \InprocServer32                = mscoree.dll, ThreadingModel=Both,
                                         Class, Assembly, RuntimeVersion, CodeBase
        \InprocServer32\<assembly ver> = same values (regasm parity)
        \ProgId                        = ProgId
        \Implemented Categories\{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}   (.NET)
      HKLM\SOFTWARE\Classes\<ProgId>   = ProgId, \CLSID = {guid}
      HKLM\SOFTWARE\SolidWorks\AddIns\{guid}     Title / Description
      HKLM + HKCU \SOFTWARE\SolidWorks\AddInsStartup\{guid} = 1

    HKLM needs administrator rights, so the script relaunches itself elevated
    when -Elevate is passed. Every machine-wide write falls back to reg.exe if
    the .NET registry API is blocked (some endpoint-protection products do that).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Install-Addin.ps1 -Elevate
    powershell -ExecutionPolicy Bypass -File tools\Install-Addin.ps1 -Elevate -InstallDirectory "D:\MechKit"
    powershell -ExecutionPolicy Bypass -File tools\Install-Addin.ps1 -Elevate -NoCopy
#>
[CmdletBinding()]
param(
    [string] $DllPath,

    [string] $InstallDirectory,

    # Register the built DLL in place instead of copying it
    [switch] $NoCopy,

    # Legacy switch, kept so old command lines keep working
    [switch] $Machine,

    # Relaunch elevated (UAC prompt) when not already an administrator
    [switch] $Elevate,

    [switch] $NoStartup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
# No spaces: the CodeBase registry value is plain text and some SOLIDWORKS
# builds fail to load an add-in from a path that contains spaces / %20.
$defaultInstallDirectory = 'C:\MechKit'
$dotNetCategory = '{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}'
# Bare file name: InprocServer32 is REG_SZ, so %SystemRoot% would NOT be
# expanded and CoCreateInstance would fail with 0x8007007E.
$mscoree = 'mscoree.dll'
$runtimeVersion = 'v4.0.30319'

function Test-IsElevated {
    return ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

# ---------------------------------------------------------------- elevation
if (-not (Test-IsElevated)) {
    if (-not $Elevate) {
        throw 'Administrator rights are required (SOLIDWORKS reads add-ins from HKLM). Re-run with -Elevate.'
    }

    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -Elevate' -f $PSCommandPath
    if ($InstallDirectory) { $arguments += ' -InstallDirectory "{0}"' -f $InstallDirectory }
    if ($DllPath) { $arguments += ' -DllPath "{0}"' -f $DllPath }
    if ($NoCopy) { $arguments += ' -NoCopy' }
    if ($NoStartup) { $arguments += ' -NoStartup' }

    Write-Host 'Requesting administrator rights (a UAC prompt will appear)...'
    $process = Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Verb RunAs -Wait -PassThru
    exit $process.ExitCode
}

# The elevated child owns its console window, so keep a transcript to inspect.
$transcript = Join-Path $env:TEMP 'MechKit-install.log'
try { Start-Transcript -Path $transcript -Force | Out-Null } catch { }

trap {
    Write-Host ''
    Write-Host ("INSTALL FAILED: {0}" -f $_.Exception.Message)
    Write-Host $_.ScriptStackTrace
    try { Stop-Transcript | Out-Null } catch { }
    exit 1
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$administrators = New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')
$administratorsEnabled = $false
foreach ($group in $identity.Groups) {
    if ($group.Value -eq $administrators.Value) { $administratorsEnabled = $true }
}

Write-Host ("Elevated  : {0} (Administrators token: {1})" -f $identity.Name, $administratorsEnabled)
Write-Host ("Process   : {0}-bit, PowerShell {1}" -f
    $(if ([Environment]::Is64BitProcess) { 64 } else { 32 }), $PSVersionTable.PSVersion)

# --------------------------------------------------------- registry helpers
function Invoke-RegAdd {
    param(
        [string] $SubKey,
        [string] $Name,
        [string] $Value,
        [string] $Kind
    )

    $full = "HKLM\$SubKey"
    $regType = if ($Kind -eq 'DWord') { 'REG_DWORD' } else { 'REG_SZ' }
    $regArguments = @('add', $full)

    if ([string]::IsNullOrEmpty($Name)) {
        $regArguments += '/ve'
    }
    else {
        $regArguments += @('/v', $Name)
    }

    $regArguments += @('/t', $regType, '/d', $Value, '/f')

    & reg.exe @regArguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host ("    reg.exe add failed ({0}) for {1}" -f $LASTEXITCODE, $full)
        return $false
    }

    return $true
}

function New-MachineKey {
    param([string] $SubKey)

    try {
        $key = [Microsoft.Win32.Registry]::LocalMachine.CreateSubKey($SubKey)
        if ($key) { return $key }
    }
    catch {
        Write-Host ("    default view blocked: {0}" -f $_.Exception.Message)
    }

    try {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
            [Microsoft.Win32.RegistryHive]::LocalMachine,
            [Microsoft.Win32.RegistryView]::Registry64)
        $key = $base.CreateSubKey($SubKey)
        if ($key) { return $key }
    }
    catch {
        Write-Host ("    64-bit view blocked: {0}" -f $_.Exception.Message)
    }

    return $null
}

function Set-MachineValue {
    param(
        [string] $SubKey,
        [string] $Name,
        [string] $Value,
        [string] $Kind = 'String'
    )

    $key = New-MachineKey -SubKey $SubKey
    if ($key) {
        try {
            $registryKind = if ($Kind -eq 'DWord') {
                [Microsoft.Win32.RegistryValueKind]::DWord
            }
            else {
                [Microsoft.Win32.RegistryValueKind]::String
            }

            $data = if ($Kind -eq 'DWord') { [int] $Value } else { $Value }

            if ([string]::IsNullOrEmpty($Name)) {
                $key.SetValue($null, $data, $registryKind)
            }
            else {
                $key.SetValue($Name, $data, $registryKind)
            }

            return $true
        }
        catch {
            Write-Host ("    .NET write blocked: {0}" -f $_.Exception.Message)
        }
        finally {
            $key.Close()
        }
    }

    return Invoke-RegAdd -SubKey $SubKey -Name $Name -Value $Value -Kind $Kind
}

function Remove-KeyTree {
    param([Microsoft.Win32.RegistryKey] $Root, [string] $SubKey)

    try { $Root.DeleteSubKeyTree($SubKey, $false) } catch { }
}

# ------------------------------------------------------------ file payload
function Resolve-AddinDll {
    param([string] $Candidate)

    if ($Candidate) {
        $resolved = Resolve-Path -LiteralPath $Candidate -ErrorAction SilentlyContinue
        if (-not $resolved) { throw "Assembly not found: $Candidate" }
        return $resolved.Path
    }

    foreach ($configuration in @('Release', 'Debug')) {
        $path = Join-Path $repoRoot "src\MechKit\bin\$configuration\MechKit.dll"
        if (Test-Path $path) { return (Resolve-Path -LiteralPath $path).Path }
    }

    throw 'MechKit.dll not found. Build first: powershell -File build.ps1'
}

function Get-AddinMetadata {
    param([string] $Path)

    $assembly = [System.Reflection.Assembly]::LoadFrom($Path)
    $type = $assembly.GetType('MechKit.MechKitAddin', $false)
    if (-not $type) { throw "Type MechKit.MechKitAddin not found in $Path" }

    $constants = $assembly.GetType('MechKit.AddinConstants', $false)

    return [pscustomobject]@{
        Guid         = '{' + $type.GUID.ToString().ToUpperInvariant() + '}'
        TypeName     = $type.FullName
        Assembly     = $assembly.FullName
        AssemblyName = $assembly.GetName().Name
        Version      = $assembly.GetName().Version.ToString()
        Title        = $constants.GetField('Title').GetValue($null)
        Description  = $constants.GetField('Description').GetValue($null)
        ProgId       = $constants.GetField('ProgId').GetValue($null)
    }
}

function Copy-AddinPayload {
    param([string] $SourceDll, [string] $TargetDirectory)

    if (-not (Test-Path $TargetDirectory)) {
        New-Item -ItemType Directory -Path $TargetDirectory -Force | Out-Null
    }

    $sourceDirectory = Split-Path -Parent $SourceDll
    foreach ($name in @(
        'MechKit.dll',
        'SolidWorks.Interop.sldworks.dll',
        'SolidWorks.Interop.swconst.dll',
        'SolidWorks.Interop.swpublished.dll'
    )) {
        $source = Join-Path $sourceDirectory $name
        if (-not (Test-Path $source)) { continue }
        Copy-Item -LiteralPath $source -Destination (Join-Path $TargetDirectory $name) -Force
    }

    Write-Host "Files copied to $TargetDirectory"
}

# ------------------------------------------------------------------- main
$dll = Resolve-AddinDll -Candidate $DllPath

if (-not $NoCopy) {
    $target = if ($InstallDirectory) { $InstallDirectory } else { $defaultInstallDirectory }
    Copy-AddinPayload -SourceDll $dll -TargetDirectory $target
    $dll = Join-Path $target 'MechKit.dll'
}

$meta = Get-AddinMetadata -Path $dll
$startup = if ($NoStartup) { 0 } else { 1 }
$codeBase = 'file:///' + ($dll -replace '\\','/')

Write-Host ''
Write-Host ("Add-in   : {0}" -f $meta.Title)
Write-Host ("CLSID    : {0}" -f $meta.Guid)
Write-Host ("ProgId   : {0}" -f $meta.ProgId)
Write-Host ("Assembly : {0}" -f $dll)
Write-Host ''

# Drop the development-time per-user registration: its CodeBase would shadow
# the machine-wide one.
Remove-KeyTree -Root ([Microsoft.Win32.Registry]::CurrentUser) -SubKey "Software\Classes\CLSID\$($meta.Guid)"
Remove-KeyTree -Root ([Microsoft.Win32.Registry]::CurrentUser) -SubKey "Software\Classes\$($meta.ProgId)"
Remove-KeyTree -Root ([Microsoft.Win32.Registry]::CurrentUser) -SubKey "SOFTWARE\SolidWorks\AddIns\$($meta.Guid)"

# --- machine-wide COM registration (regasm /codebase layout) --------------
$clsidPath = "SOFTWARE\Classes\CLSID\$($meta.Guid)"
$ok = $true

$ok = (Set-MachineValue -SubKey $clsidPath -Name '' -Value $meta.ProgId) -and $ok
$ok = (Set-MachineValue -SubKey "$clsidPath\InprocServer32" -Name '' -Value $mscoree) -and $ok
$ok = (Set-MachineValue -SubKey "$clsidPath\InprocServer32" -Name 'ThreadingModel' -Value 'Both') -and $ok
$ok = (Set-MachineValue -SubKey "$clsidPath\InprocServer32" -Name 'Class' -Value $meta.TypeName) -and $ok
$ok = (Set-MachineValue -SubKey "$clsidPath\InprocServer32" -Name 'Assembly' -Value $meta.Assembly) -and $ok
$ok = (Set-MachineValue -SubKey "$clsidPath\InprocServer32" -Name 'RuntimeVersion' -Value $runtimeVersion) -and $ok
$ok = (Set-MachineValue -SubKey "$clsidPath\InprocServer32" -Name 'CodeBase' -Value $codeBase) -and $ok

$versioned = "$clsidPath\InprocServer32\$($meta.Version)"
$ok = (Set-MachineValue -SubKey $versioned -Name 'Class' -Value $meta.TypeName) -and $ok
$ok = (Set-MachineValue -SubKey $versioned -Name 'Assembly' -Value $meta.Assembly) -and $ok
$ok = (Set-MachineValue -SubKey $versioned -Name 'RuntimeVersion' -Value $runtimeVersion) -and $ok
$ok = (Set-MachineValue -SubKey $versioned -Name 'CodeBase' -Value $codeBase) -and $ok

$ok = (Set-MachineValue -SubKey "$clsidPath\ProgId" -Name '' -Value $meta.ProgId) -and $ok
$ok = (Set-MachineValue -SubKey "$clsidPath\Implemented Categories\$dotNetCategory" -Name '' -Value '') -and $ok
$ok = (Set-MachineValue -SubKey "SOFTWARE\Classes\$($meta.ProgId)" -Name '' -Value $meta.ProgId) -and $ok
$ok = (Set-MachineValue -SubKey "SOFTWARE\Classes\$($meta.ProgId)\CLSID" -Name '' -Value $meta.Guid) -and $ok

if (-not $ok) {
    throw 'Could not write the machine-wide COM registration (HKLM\SOFTWARE\Classes).'
}

Write-Host "COM registered : HKLM\SOFTWARE\Classes\CLSID\$($meta.Guid)"

# --- SOLIDWORKS add-in list ----------------------------------------------
$addInPath = "SOFTWARE\SolidWorks\AddIns\$($meta.Guid)"
$ok = (Set-MachineValue -SubKey $addInPath -Name '' -Value '0' -Kind 'DWord') -and $ok
$ok = (Set-MachineValue -SubKey $addInPath -Name 'Title' -Value $meta.Title) -and $ok
$ok = (Set-MachineValue -SubKey $addInPath -Name 'Description' -Value $meta.Description) -and $ok

if (-not $ok) {
    throw 'Could not write HKLM\SOFTWARE\SolidWorks\AddIns.'
}

Write-Host "Add-in listed  : HKLM\SOFTWARE\SolidWorks\AddIns\$($meta.Guid)"

# --- load at startup (machine default + current user) ---------------------
$startupPath = "SOFTWARE\SolidWorks\AddInsStartup\$($meta.Guid)"
Set-MachineValue -SubKey $startupPath -Name '' -Value $startup -Kind 'DWord' | Out-Null

$userStartup = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey(
    "SOFTWARE\SolidWorks\AddInsStartup\$($meta.Guid)")
try {
    $userStartup.SetValue($null, $startup, [Microsoft.Win32.RegistryValueKind]::DWord)
}
finally {
    $userStartup.Close()
}

Write-Host ("Start at launch: {0}" -f ($startup -eq 1))
Write-Host ''
Write-Host 'Done. Restart SOLIDWORKS:'
Write-Host '  * CommandManager gets a new add-in tab'
Write-Host '  * a toolbar with the same name appears'
Write-Host '  * Tools > Add-Ins lists the add-in with Start Up checked'

try { Stop-Transcript | Out-Null } catch { }
