$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$bin=Join-Path $repo 'src\MechKit\bin\Release'
Add-Type -Path (Join-Path $bin 'SolidWorks.Interop.sldworks.dll')
Add-Type -Path (Join-Path $PSScriptRoot 'testdata\BomRenameChecks.cs') -ReferencedAssemblies @((Join-Path $bin 'SolidWorks.Interop.sldworks.dll'),'System.Core')
[BomRenameChecks]::Run((Join-Path $repo ('artifacts\bom-rename-'+[Guid]::NewGuid().ToString('N'))),(Join-Path $bin 'MechKit.dll'))