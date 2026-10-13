param([string]$PreviewPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
$repo=Split-Path -Parent $PSScriptRoot
$a=[Reflection.Assembly]::LoadFrom("$repo\src\MechKit\bin\Release\MechKit.dll")
$t=$a.GetType('MechKit.Core.QuickPartsLibrary')
$root=Join-Path $repo ('artifacts\library-tests-'+[Guid]::NewGuid().ToString('N'))
$library=[Activator]::CreateInstance($t,[object[]]@([string]$root))
function Check($ok,$message) { if(-not $ok){throw $message} }
$version=$t.GetMethod('Is2024')
Check ($version.Invoke($null,@(,[string[]]@('16000,1','17000,2')))) '2024 version history'
Check (-not $version.Invoke($null,@(,[string[]]@('17000,1','18000,2')))) 'Newer version accepted'
Check (-not $version.Invoke($null,@(,[string[]]@('16000,1')))) 'Older version accepted'
Check (-not $version.Invoke($null,@(,[string[]]@('bad')))) 'Unknown version accepted'
Check ((Get-ChildItem -LiteralPath $root -Directory).Count -eq 4) 'Empty categories'
$source=Join-Path $root '气动件\SMC-气缸.SLDPRT'
[IO.File]::WriteAllText($source,'fixture bytes only - not a real SW model')
$ignored=Join-Path $root '气动件\~$SMC-气缸.SLDPRT'
[IO.File]::WriteAllText($ignored,'lock')
$found=$library.Search('全部分类','smc 气缸')
Check ($found.Count -eq 1) 'Search words / case / lock files'
Check ($library.Search('电控件','SMC').Count -eq 0) 'Category filter'
$destination=Join-Path $root 'copies'
[IO.Directory]::CreateDirectory($destination)|Out-Null
$copy=$t.GetMethod('CopyUnique')
$first=$copy.Invoke($null,@([string]$source,[string]$destination))
$second=$copy.Invoke($null,@([string]$source,[string]$destination))
Check ($first -ne $second) 'Collision overwrote existing file'
Check ((Get-FileHash -LiteralPath $first).Hash -eq (Get-FileHash -LiteralPath $source).Hash) 'Copy bytes differ'
Check ((Get-FileHash -LiteralPath $second).Hash -eq (Get-FileHash -LiteralPath $source).Hash) 'Second copy bytes differ'
$before=(Get-ChildItem -LiteralPath $destination).Count
$failed=$false
try { $copy.Invoke($null,@([string](Join-Path $root 'missing.sldprt'),[string]$destination)) } catch { $failed=$true }
Check $failed 'Missing source accepted'
Check ((Get-ChildItem -LiteralPath $destination).Count -eq $before) 'Failed copy left partial file'
# Use the empty real library layout, never add fixture bytes to the user's library.
$empty=Join-Path $root 'empty'
$uiLibrary=[Activator]::CreateInstance($t,[object[]]@([string]$empty))
$addin=[Activator]::CreateInstance($a.GetType('MechKit.MechKitAddin'))
$panel=[Activator]::CreateInstance($a.GetType('MechKit.UI.PartsLibraryPanel'),[Reflection.BindingFlags]'Instance,NonPublic',$null,@($addin,$uiLibrary),$null)
$form=New-Object Windows.Forms.Form
$form.ClientSize=New-Object Drawing.Size 360,750
$form.ShowInTaskbar=$false
$form.Opacity=0
$form.Controls.Add($panel)
try {
 $form.Show()
 [Windows.Forms.Application]::DoEvents()
 $layout=$panel.Controls[0]
 Check ($layout.GetControlFromPosition(0,5).Height -gt 350) 'List lost available space'
 foreach($button in $layout.GetControlFromPosition(0,1).Controls) { Check ($button.Width -ge $button.PreferredSize.Width) 'Clipped action' }
 if($PreviewPath) {
  $bitmap=New-Object Drawing.Bitmap $form.Width,$form.Height
  $form.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle 0,0,$form.Width,$form.Height))
  $bitmap.Save($PreviewPath)
  $bitmap.Dispose()
 }
} finally { $form.Dispose() }
Write-Host 'PASS: versions, empty categories, search, collision, copy rollback, narrow panel layout'