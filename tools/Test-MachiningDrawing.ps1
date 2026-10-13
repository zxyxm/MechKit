param([string]$Part)
$ErrorActionPreference='Stop'
$bin=Join-Path $PSScriptRoot '..\src\MechKit\bin\Release'
$a=[Reflection.Assembly]::LoadFrom((Join-Path $bin 'MechKit.dll'))
$t=$a.GetType('MechKit.Features.MachiningDrawingBuilder',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$fit=$t.GetMethod('FitScale',$flags)
foreach($size in @(0.001,0.025,0.10,0.40,1.0,4.0)) {
 $scale=$fit.Invoke($null,@([double]$size))
 if($size*$scale -gt 0.050000001 -or $scale -le 0) {throw 'View does not fit its reserved area'}
}
foreach($size in @(0,-1,[double]::NaN,[double]::PositiveInfinity)) {
 $rejected=$false
 try {$fit.Invoke($null,@([double]$size))} catch {$rejected=$true}
 if(-not $rejected){throw 'Invalid bounds accepted'}
}
$key=$t.GetMethod('DimensionKey',$flags)
if($key.Invoke($null,@('D1@Sketch1@Part.SLDPRT')) -ne 'D1@Sketch1'){throw 'Dimension identity includes document suffix'}
if(-not $a.GetType('MechKit.MechKitAddin').GetMethod('OnMachiningDrawing')){throw 'Button callback missing'}
Write-Host 'Offline machining drawing checks passed.'
if($Part) {
 Add-Type -Path (Join-Path $bin 'SolidWorks.Interop.sldworks.dll')
 $refs=@((Join-Path $bin 'SolidWorks.Interop.sldworks.dll'),(Join-Path $bin 'SolidWorks.Interop.swpublished.dll'),(Join-Path $bin 'MechKit.dll'),'System.Core')
 Add-Type -Path (Join-Path $PSScriptRoot 'testdata\MachiningDrawingLiveTest.cs') -ReferencedAssemblies $refs
 [MachiningDrawingLiveTest]::Run($Part,(Join-Path $PSScriptRoot ('..\artifacts\machining-live-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))))
}
