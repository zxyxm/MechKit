<#
.SYNOPSIS
    Builds the release package: a folder laid out like the Ican toolbox
    (payload DLLs + installer exe + readme + config.ini).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build-dist.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $SolidWorksPath,

    [string] $OutputRoot,

    # Also refresh the source folder from this working copy
    [switch] $IncludeSource
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$manifestPath = Join-Path $repoRoot 'installer\dist-manifest.json'
$manifest = (Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8) | ConvertFrom-Json

$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
if (-not (Test-Path $msbuild)) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $msbuild = (& $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
            -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1)
    }
}

if (-not (Test-Path $msbuild)) { throw 'MSBuild not found.' }

# ---------------------------------------------------------------- 1. build
Write-Host '=== build add-in ==='
& (Join-Path $repoRoot 'build.ps1') -Configuration $Configuration -SolidWorksPath:$SolidWorksPath

$addinDll = Join-Path $repoRoot "src\MechKit\bin\$Configuration\MechKit.dll"
if (-not (Test-Path $addinDll)) { throw "Missing $addinDll" }

$setupProject = Join-Path $repoRoot 'installer\MechKitSetup\MechKitSetup.csproj'
$swFolder = (Get-ItemProperty 'HKLM:\SOFTWARE\SolidWorks\SOLIDWORKS 2024\Setup').'SolidWorks Folder'
if ($SolidWorksPath) { $swFolder = $SolidWorksPath }

Write-Host ''
Write-Host '=== build installer ==='
& $msbuild $setupProject /nologo /restore /v:minimal "/p:Configuration=$Configuration" "/p:SwDir=$swFolder"
if ($LASTEXITCODE -ne 0) { throw "Installer build failed ($LASTEXITCODE)." }

$setupExe = Join-Path $repoRoot "installer\MechKitSetup\bin\$Configuration\MechKitSetup.exe"
if (-not (Test-Path $setupExe)) { throw "Missing $setupExe" }

# ------------------------------------------------------------- 2. assemble
$version = [System.Reflection.AssemblyName]::GetAssemblyName($addinDll).Version
$folderName = '{0} V{1}.{2}' -f $manifest.folderPrefix, $version.Major, $version.Minor

if (-not $OutputRoot) { $OutputRoot = Join-Path $repoRoot 'dist' }
$target = Join-Path $OutputRoot $folderName
$packageDir = Join-Path $target $manifest.packageFolderName
$sourceDir = Join-Path $target $manifest.sourceFolderName

# Safety: only ever delete a directory that really is inside the output root.
$resolvedRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$resolvedTarget = [System.IO.Path]::GetFullPath($target)
if (-not $resolvedTarget.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $resolvedTarget -eq $resolvedRoot) {
    throw "Refusing to clean unexpected path: $resolvedTarget"
}

# By default only the package folder is rebuilt; the source folder is the
# working copy and is left untouched. Use -IncludeSource to refresh it too.
if ($IncludeSource) {
    if (Test-Path $target) { Remove-Item -LiteralPath $target -Recurse -Force }
}
elseif (Test-Path $packageDir) {
    $resolvedPackage = [System.IO.Path]::GetFullPath($packageDir)
    if (-not $resolvedPackage.StartsWith($resolvedTarget, [StringComparison]::OrdinalIgnoreCase) -or
        $resolvedPackage -eq $resolvedTarget) {
        throw "Refusing to clean unexpected path: $resolvedPackage"
    }

    Remove-Item -LiteralPath $packageDir -Recurse -Force
}

New-Item -ItemType Directory -Path $packageDir -Force | Out-Null

Write-Host ''
Write-Host "=== assemble $target ==="
Write-Host ("  [{0}]" -f $manifest.packageFolderName)

# payload: add-in + interop assemblies
$payloadSource = Split-Path -Parent $addinDll
foreach ($name in @(
    'MechKit.dll',
    'SolidWorks.Interop.sldworks.dll',
    'SolidWorks.Interop.swconst.dll',
    'SolidWorks.Interop.swpublished.dll'
)) {
    $source = Join-Path $payloadSource $name
    if (Test-Path $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $packageDir $name) -Force
        Write-Host "    + $name"
    }
}

# installer / uninstaller
Copy-Item -LiteralPath $setupExe -Destination (Join-Path $packageDir $manifest.installerExeName) -Force
Write-Host "    + $($manifest.installerExeName)"

# documentation
Copy-Item -LiteralPath (Join-Path $repoRoot 'installer\dist\readme-template.txt') `
    -Destination (Join-Path $packageDir $manifest.readmeName) -Force
Write-Host "    + $($manifest.readmeName)"

Copy-Item -LiteralPath (Join-Path $repoRoot 'installer\dist\config-template.ini') `
    -Destination (Join-Path $packageDir $manifest.configName) -Force
Write-Host "    + $($manifest.configName)"

# diagnostics tool (optional, handy for the user)
$inspector = Join-Path $repoRoot "tools\DocInspector\bin\$Configuration\DocInspector.exe"
if (Test-Path $inspector) {
    $diagFolder = Join-Path $packageDir $manifest.diagnosticsFolder
    New-Item -ItemType Directory -Path $diagFolder -Force | Out-Null
    foreach ($name in @('DocInspector.exe', 'DocInspector.exe.config', 'MechKit.dll',
                        'SolidWorks.Interop.sldworks.dll', 'SolidWorks.Interop.swconst.dll',
                        'SolidWorks.Interop.swpublished.dll')) {
        $source = Join-Path (Split-Path -Parent $inspector) $name
        if (Test-Path $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $diagFolder $name) -Force }
    }
    Write-Host ("    + {0}\DocInspector.exe" -f $manifest.diagnosticsFolder)
}

# source tree, excluding build output (only when explicitly requested:
# the source folder is the working copy, so it must not be overwritten casually)
if ($IncludeSource) {
Write-Host ("  [{0}]" -f $manifest.sourceFolderName)
New-Item -ItemType Directory -Path $sourceDir -Force | Out-Null
$robocopyArgs = @(
    $repoRoot,
    $sourceDir,
    '/E',
    '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/R:1', '/W:1',
    '/XD',
    (Join-Path $repoRoot 'bin'),
    (Join-Path $repoRoot 'obj'),
    (Join-Path $repoRoot '.git'),
    (Join-Path $repoRoot '.vs'),
    (Join-Path $repoRoot 'dist'),
    (Join-Path $repoRoot 'src\MechKit\bin'),
    (Join-Path $repoRoot 'src\MechKit\obj'),
    (Join-Path $repoRoot 'tools\DocInspector\bin'),
    (Join-Path $repoRoot 'tools\DocInspector\obj'),
    (Join-Path $repoRoot 'installer\MechKitSetup\bin'),
    (Join-Path $repoRoot 'installer\MechKitSetup\obj')
)

& robocopy @robocopyArgs | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)." }
Write-Host '    + full source tree (bin/obj/.git excluded)'
}

Copy-Item -LiteralPath (Join-Path $repoRoot 'installer\dist\package-readme-template.txt') `
    -Destination (Join-Path $target $manifest.guideName) -Force
Write-Host ("  [{0}]" -f $manifest.guideName)

Write-Host ''
Write-Host 'Package contents:'
Get-ChildItem -LiteralPath $target -Recurse | ForEach-Object {
    $relative = $_.FullName.Substring($target.Length + 1)
    if ($_.PSIsContainer) { Write-Host ("  [dir] {0}" -f $relative) }
    else { Write-Host ("  {0,10:N0}  {1}" -f $_.Length, $relative) }
}

Write-Host ''
Write-Host "Output: $target"
