[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if (Get-Process SLDWORKS -ErrorAction SilentlyContinue) {
    throw 'Close SOLIDWORKS after saving documents before updating registered DLLs.'
}
$repo = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repo 'src/MechKit/bin/Release/MechKit.dll'
$suffix = 'Software\Classes\CLSID\{A7F3C1E2-5B4D-4E8A-9C21-3D6F0B7A5E10}\InprocServer32'
$targets = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($hive in @('HKEY_CURRENT_USER', 'HKEY_LOCAL_MACHINE')) {
    $root = 'Registry::' + $hive + '\' + $suffix
    if (-not (Test-Path $root)) { continue }
    foreach ($key in @((Get-Item $root)) + @(Get-ChildItem $root)) {
        $value = (Get-ItemProperty $key.PSPath).CodeBase
        if ($value) { [void]$targets.Add(([Uri]$value).LocalPath) }
    }
}
if ($targets.Count -eq 0) { throw 'No registered add-in paths found.' }
$backup = Get-Date -Format 'yyyyMMdd-HHmmss'
$expected = (Get-FileHash -LiteralPath $source).Hash
foreach ($target in $targets) {
    if ([IO.Path]::GetFileName($target) -ne 'MechKit.dll') { throw "Unexpected registration: $target" }
    if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination ($target + '.backup-' + $backup) }
    Copy-Item -LiteralPath $source -Destination $target -Force
    if ((Get-FileHash -LiteralPath $target).Hash -ne $expected) { throw "Verification failed: $target" }
    Write-Host "Updated and verified: $target"
}
