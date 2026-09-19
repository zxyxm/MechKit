<#
.SYNOPSIS
    Builds the MechKit SOLIDWORKS add-in.

.DESCRIPTION
    Locates the SOLIDWORKS installation (registry first, then the default
    install folders), locates MSBuild, then builds the add-in as x64 / net48.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build.ps1
    powershell -ExecutionPolicy Bypass -File build.ps1 -Configuration Debug
    powershell -ExecutionPolicy Bypass -File build.ps1 -SolidWorksPath "D:\SW\SOLIDWORKS"
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $SolidWorksPath,

    [switch] $SkipTools
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot 'src\MechKit\MechKit.csproj'
$toolsProject = Join-Path $repoRoot 'tools\DocInspector\DocInspector.csproj'
$harnessProject = Join-Path $repoRoot 'tools\MechKitHarness\MechKitHarness.csproj'

function Test-SolidWorksFolder {
    param([string] $Folder)

    if ([string]::IsNullOrWhiteSpace($Folder)) { return $false }
    return (Test-Path (Join-Path $Folder 'SolidWorks.Interop.sldworks.dll'))
}

function Resolve-SolidWorksFolder {
    param([string] $Override)

    if ($Override) {
        $Override = $Override.TrimEnd('\')
        if (Test-SolidWorksFolder $Override) { return $Override }
        throw "SOLIDWORKS interop assemblies not found in '$Override'."
    }

    $candidates = New-Object System.Collections.Generic.List[object]

    foreach ($root in @('HKLM:\SOFTWARE\SolidWorks', 'HKLM:\SOFTWARE\WOW6432Node\SolidWorks')) {
        if (-not (Test-Path $root)) { continue }

        foreach ($key in (Get-ChildItem $root -ErrorAction SilentlyContinue)) {
            if ($key.PSChildName -notmatch '^SOLIDWORKS \d{4}$') { continue }

            $setupKey = Join-Path $key.PSPath 'Setup'
            if (-not (Test-Path $setupKey)) { continue }

            $setup = Get-ItemProperty -Path $setupKey -ErrorAction SilentlyContinue
            $folder = $null
            if ($setup -and ($setup.PSObject.Properties.Name -contains 'SolidWorks Folder')) {
                $folder = $setup.'SolidWorks Folder'
            }

            if ($folder) {
                $candidates.Add([pscustomobject]@{
                    Version = $key.PSChildName
                    Folder  = $folder.TrimEnd('\')
                })
            }
        }
    }

    foreach ($candidate in ($candidates | Sort-Object -Property Version -Descending)) {
        if (Test-SolidWorksFolder $candidate.Folder) { return $candidate.Folder }
    }

    foreach ($fallback in @(
        'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS (2)',
        'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS',
        'C:\Program Files\SolidWorks Corp\SolidWorks'
    )) {
        if (Test-SolidWorksFolder $fallback) { return $fallback }
    }

    throw 'SOLIDWORKS installation not found. Pass -SolidWorksPath <folder>.'
}

function Resolve-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null
        if ($found) { return ($found | Select-Object -First 1) }
    }

    foreach ($pattern in @(
        'C:\Program Files\Microsoft Visual Studio\2022\*\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files (x86)\Microsoft Visual Studio\2019\*\MSBuild\Current\Bin\MSBuild.exe'
    )) {
        $match = Get-ChildItem -Path $pattern -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($match) { return $match.FullName }
    }

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) { return $dotnet.Source }

    throw 'MSBuild not found. Install Visual Studio 2022 with the .NET desktop workload.'
}

$swFolder = Resolve-SolidWorksFolder -Override $SolidWorksPath
Write-Host "SOLIDWORKS interop: $swFolder"

$msbuild = Resolve-MSBuild
Write-Host "Build tool        : $msbuild"

$arguments = @(
    $project,
    '/nologo',
    '/restore',
    '/v:minimal',
    "/p:Configuration=$Configuration",
    "/p:SwDir=$swFolder"
)

& $msbuild $arguments
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

$output = Join-Path $repoRoot "src\MechKit\bin\$Configuration\MechKit.dll"
if (-not (Test-Path $output)) {
    throw "Build succeeded but output not found: $output"
}

Write-Host ''
Write-Host "Output: $output"

# DocInspector links against MechKit.dll, so it must be built afterwards
if (-not $SkipTools) {
    Write-Host ''
    Write-Host 'Building tools\DocInspector ...'

    $toolkitDir = Join-Path $repoRoot "src\MechKit\bin\$Configuration"

    $toolArguments = @(
        $toolsProject,
        '/nologo',
        '/restore',
        '/v:minimal',
        "/p:Configuration=$Configuration",
        "/p:SwDir=$swFolder",
        "/p:MechKitDir=$toolkitDir"
    )

    & $msbuild $toolArguments
    if ($LASTEXITCODE -ne 0) {
        throw "DocInspector build failed with exit code $LASTEXITCODE."
    }

    $toolsOutput = Join-Path $repoRoot "tools\DocInspector\bin\$Configuration\DocInspector.exe"
    Write-Host "Output: $toolsOutput"

    Write-Host ''
    Write-Host 'Building tools\MechKitHarness ...'

    $harnessArguments = @(
        $harnessProject,
        '/nologo',
        '/restore',
        '/v:minimal',
        "/p:Configuration=$Configuration",
        "/p:SwDir=$swFolder",
        "/p:MechKitDir=$toolkitDir"
    )

    & $msbuild $harnessArguments
    if ($LASTEXITCODE -ne 0) {
        throw "MechKitHarness build failed with exit code $LASTEXITCODE."
    }

    $harnessOutput = Join-Path $repoRoot "tools\MechKitHarness\bin\$Configuration\MechKitHarness.exe"
    Write-Host "Output: $harnessOutput"
}
