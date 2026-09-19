<#
.SYNOPSIS
    Updates MechKit's semantic version from the size and compatibility impact of
    changes made since the last successful release package.

.DESCRIPTION
    Auto classification:
      patch - documentation/resources or a small code fix
      minor - a broader feature change (many files, new code, or large line delta)
      major - COM identity or target framework compatibility changed

    build-dist.ps1 calls this before building, then commits the new snapshot only
    after the package succeeds. Re-running a failed build is therefore idempotent.
#>
[CmdletBinding()]
param(
    [ValidateSet('Auto', 'None', 'Patch', 'Minor', 'Major')]
    [string] $Level = 'Auto',

    [switch] $CommitState,

    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$assemblyInfo = Join-Path $RepoRoot 'src\MechKit\Properties\AssemblyInfo.cs'
$projectFile = Join-Path $RepoRoot 'src\MechKit\MechKit.csproj'
$constantsFile = Join-Path $RepoRoot 'src\MechKit\AddinConstants.cs'
$stateFile = Join-Path $RepoRoot '.release-state.json'

function Get-CurrentVersion {
    $text = Get-Content -LiteralPath $assemblyInfo -Raw -Encoding UTF8
    $match = [regex]::Match($text, 'AssemblyVersion\("(?<version>\d+\.\d+\.\d+\.\d+)"\)')
    if (-not $match.Success) { throw 'AssemblyVersion was not found.' }
    return [version]$match.Groups['version'].Value
}

function Get-Compatibility {
    $constants = Get-Content -LiteralPath $constantsFile -Raw -Encoding UTF8
    $project = Get-Content -LiteralPath $projectFile -Raw -Encoding UTF8
    $guid = [regex]::Match($constants, 'AddinGuid\s*=\s*"(?<value>[^"]+)"').Groups['value'].Value
    $progId = [regex]::Match($constants, 'ProgId\s*=\s*"(?<value>[^"]+)"').Groups['value'].Value
    $framework = [regex]::Match($project, '<TargetFramework>(?<value>[^<]+)</TargetFramework>').Groups['value'].Value
    return [ordered]@{ addinGuid = $guid; progId = $progId; targetFramework = $framework }
}

function Get-ReleaseFiles {
    $paths = & git -c core.quotepath=false -C $RepoRoot ls-files --cached --others --exclude-standard
    if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate repository files.' }
    return $paths | Where-Object {
        $_ -and
        $_ -notmatch '^(artifacts|dist|dist-latest|dist-next|src/MechKit/(bin|obj)|tools/[^/]+/(bin|obj)|installer/MechKitSetup/(bin|obj))/' -and
        $_ -ne '.release-state.json' -and
        $_ -ne 'src/MechKit/Properties/AssemblyInfo.cs'
    }
}

function Get-Snapshot {
    $items = New-Object System.Collections.Generic.List[object]
    foreach ($relative in Get-ReleaseFiles) {
        $full = Join-Path $RepoRoot ($relative -replace '/', '\')
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }
        $item = Get-Item -LiteralPath $full
        $lines = 0
        if ($item.Extension -match '^\.(cs|csproj|ps1|json|md|txt|ini)$') {
            $lines = @(Get-Content -LiteralPath $full -Encoding UTF8).Count
        }
        $items.Add([ordered]@{
            path = $relative
            hash = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash
            bytes = $item.Length
            lines = $lines
        })
    }
    return $items
}

function Get-AutoLevel($state, $snapshot, $compatibility) {
    if ($null -eq $state) { return 'None' }
    if ($state.compatibility.addinGuid -ne $compatibility.addinGuid -or
        $state.compatibility.progId -ne $compatibility.progId -or
        $state.compatibility.targetFramework -ne $compatibility.targetFramework) {
        return 'Major'
    }

    $before = @{}
    foreach ($file in $state.files) { $before[$file.path] = $file }
    $after = @{}
    foreach ($file in $snapshot) { $after[$file.path] = $file }
    $changed = New-Object System.Collections.Generic.List[string]
    $lineDelta = 0
    $newOrRemovedCode = $false
    foreach ($path in @($before.Keys + $after.Keys | Sort-Object -Unique)) {
        $old = $before[$path]
        $new = $after[$path]
        if ($null -eq $old -or $null -eq $new -or $old.hash -ne $new.hash) {
            $changed.Add($path)
            $oldLines = if ($null -eq $old) { 0 } else { [int]$old.lines }
            $newLines = if ($null -eq $new) { 0 } else { [int]$new.lines }
            $lineDelta += [Math]::Abs($newLines - $oldLines)
            if ($path -match '^(src/|installer/MechKitSetup/|tools/[^/]+/).+\.(cs|csproj)$' -and
                ($null -eq $old -or $null -eq $new)) {
                $newOrRemovedCode = $true
            }
        }
    }
    if ($changed.Count -eq 0) { return 'None' }

    $codeCount = @($changed | Where-Object {
        $_ -match '^(src/|installer/MechKitSetup/|tools/[^/]+/).+\.(cs|csproj)$'
    }).Count
    if ($newOrRemovedCode -or $codeCount -ge 4 -or $lineDelta -ge 80) { return 'Minor' }
    return 'Patch'
}

function Set-Version([version] $version) {
    $text = Get-Content -LiteralPath $assemblyInfo -Raw -Encoding UTF8
    $value = '{0}.{1}.{2}.0' -f $version.Major, $version.Minor, $version.Build
    $text = [regex]::Replace($text, 'AssemblyVersion\("\d+\.\d+\.\d+\.\d+"\)', "AssemblyVersion(`"$value`")")
    $text = [regex]::Replace($text, 'AssemblyFileVersion\("\d+\.\d+\.\d+\.\d+"\)', "AssemblyFileVersion(`"$value`")")
    [System.IO.File]::WriteAllText($assemblyInfo, $text, (New-Object System.Text.UTF8Encoding($false)))

    $setupProject = Join-Path $RepoRoot 'installer\MechKitSetup\MechKitSetup.csproj'
    $setupText = Get-Content -LiteralPath $setupProject -Raw -Encoding UTF8
    $shortValue = '{0}.{1}.{2}' -f $version.Major, $version.Minor, $version.Build
    $setupText = [regex]::Replace($setupText,
        '(<MechKitVersion\s+Condition="[^"]+">)\d+\.\d+\.\d+(</MechKitVersion>)',
        { param($match) $match.Groups[1].Value + $shortValue + $match.Groups[2].Value })
    [System.IO.File]::WriteAllText($setupProject, $setupText,
        (New-Object System.Text.UTF8Encoding($false)))
}

$state = if (Test-Path -LiteralPath $stateFile) {
    Get-Content -LiteralPath $stateFile -Raw -Encoding UTF8 | ConvertFrom-Json
} else { $null }
$snapshot = @(Get-Snapshot)
$compatibility = Get-Compatibility
$resolvedLevel = if ($Level -eq 'Auto') { Get-AutoLevel $state $snapshot $compatibility } else { $Level }
$current = Get-CurrentVersion
$base = if ($null -ne $state) { [version]$state.version } else { $current }

switch ($resolvedLevel) {
    'Major' { $next = [version]::new($base.Major + 1, 0, 0, 0) }
    'Minor' { $next = [version]::new($base.Major, $base.Minor + 1, 0, 0) }
    'Patch' { $next = [version]::new($base.Major, $base.Minor, $base.Build + 1, 0) }
    default { $next = $current }
}

if ($next -ne $current) { Set-Version $next }

if ($CommitState) {
    $snapshot = @(Get-Snapshot)
    $payload = [ordered]@{
        version = ('{0}.{1}.{2}.0' -f $next.Major, $next.Minor, $next.Build)
        level = $resolvedLevel.ToLowerInvariant()
        generatedAt = (Get-Date).ToString('o')
        compatibility = $compatibility
        files = $snapshot
    }
    $json = $payload | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText($stateFile, $json + [Environment]::NewLine,
        (New-Object System.Text.UTF8Encoding($false)))
}

Write-Output ('VERSION={0}.{1}.{2}' -f $next.Major, $next.Minor, $next.Build)
Write-Output ('LEVEL=' + $resolvedLevel.ToLowerInvariant())
