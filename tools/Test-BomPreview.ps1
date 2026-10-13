param([string]$Sample)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$a=[Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot '..\src\MechKit\bin\Release\MechKit.dll'))
$t=$a.GetType('MechKit.Features.BomExcelExporter')
$repair=$t.GetMethod('RepairPreviewBitmap',[Reflection.BindingFlags]'Static,NonPublic')
$png=$t.GetMethod('PreviewPng',[Reflection.BindingFlags]'Static,NonPublic')
# Two grayscale rows encoded as RLE8, deliberately missing the palette like SW.
[byte[]]$b=[byte[]]::new(64)
$b[0]=66; $b[1]=77
foreach($pair in @(@(2,64),@(10,54),@(14,40),@(18,4),@(22,2),@(30,1),@(34,10))) { [Array]::Copy([BitConverter]::GetBytes([int]$pair[1]),0,$b,$pair[0],4) }
$b[26]=1; $b[28]=8
[byte[]]$pixels=4,80,0,0,4,220,0,0,0,1
[Array]::Copy($pixels,0,$b,54,10)
[byte[]]$fixed=$repair.Invoke($null, @(,$b))
if($fixed.Length -ne 1088 -or [BitConverter]::ToInt32($fixed,10) -ne 1078) { throw 'Missing palette not repaired' }
$s=New-Object IO.MemoryStream (,$fixed)
$im=[Drawing.Bitmap]::FromStream($s)
try { foreach($point in @(@(0,0,220),@(0,1,80))) { $c=$im.GetPixel($point[0],$point[1]); if($c.R -ne $point[2] -or $c.G -ne $c.R -or $c.B -ne $c.R) { throw 'RLE pixel changed' } } } finally { $im.Dispose(); $s.Dispose() }
$fixed[54+80*4]=17; $fixed[55+80*4]=99; $fixed[56+80*4]=203
if(-not [object]::ReferenceEquals($fixed,$repair.Invoke($null,@(,$fixed)))) { throw 'Real color palette modified' }
[byte[]]$rgb=$b.Clone(); $rgb[28]=24; $rgb[30]=0
if(-not [object]::ReferenceEquals($rgb,$repair.Invoke($null,@(,$rgb)))) { throw 'RGB data modified' }
if($Sample) {
 [byte[]]$out=$png.Invoke($null,@(,[IO.File]::ReadAllBytes((Resolve-Path $Sample))))
 $output=Join-Path $PSScriptRoot '..\artifacts\verified-preview.png'
 [IO.File]::WriteAllBytes($output,$out)
 Write-Host "Live sample converted: $output"
}
Write-Host 'Preview palette regression passed.'

