param([string]$PreviewPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
$a=[Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot '..\src\MechKit\bin\Release\MechKit.dll'))
$addin=[Activator]::CreateInstance($a.GetType('MechKit.MechKitAddin'))
$settings=$a.GetType('MechKit.Core.AddinSettings').GetMethod('Load').Invoke($null,@())
$addin.GetType().GetField('_settings',[Reflection.BindingFlags]'Instance,NonPublic').SetValue($addin,$settings)
$a.GetType('MechKit.Core.IconResources').GetMethod('Extract').Invoke($null,@())
$t=$a.GetType('MechKit.UI.CommandPanel')
$panel=[Activator]::CreateInstance($t,[Reflection.BindingFlags]'Instance,NonPublic',$null,@($addin),$null)
$form=New-Object Windows.Forms.Form
$form.ClientSize=New-Object Drawing.Size 660,900
$form.Controls.Add($panel)
$form.ShowInTaskbar=$false
$form.Opacity=0
try {
 $form.Show()
 [Windows.Forms.Application]::DoEvents()
 $groups=$panel.Controls[0].GetControlFromPosition(0,0)
 if($groups.Controls.Count -ne (6 + $a.GetType('MechKit.Core.PartCategory').GetMethod('Load').Invoke($null,@($settings)).Count)) { throw 'Category count' }
 foreach($section in $groups.Controls) {
  $grid=$section.GetControlFromPosition(0,1)
  if($grid.RowCount -gt 6) { throw 'Too many rows' }
  for($i=0;$i -lt $grid.Controls.Count;$i++) {
   $button=$grid.Controls[$i]
   if($null -eq $button -or $button.BackColor -ne $section.BackColor) { throw 'Position or category background mismatch' }
   if($button.Height -lt 28 -or $button.Width -lt $button.PreferredSize.Width) { throw 'Clipped button' }
  }
 }
 if($groups.Controls[4].GetControlFromPosition(0,1).GetControlFromPosition(0,1).Text -match '材料') { throw 'Material label prefix remains' }
 $t.GetMethod('Rebuild',[Reflection.BindingFlags]'Instance,NonPublic').Invoke($panel,@())
 if($groups.Controls.Count -ne (6 + $a.GetType('MechKit.Core.PartCategory').GetMethod('Load').Invoke($null,@($settings)).Count)) { throw 'Rebuild duplicated categories' }
 [Windows.Forms.Application]::DoEvents()
 if($PreviewPath) {
  $bmp=New-Object Drawing.Bitmap $form.Width,$form.Height
  $form.DrawToBitmap($bmp,(New-Object Drawing.Rectangle 0,0,$form.Width,$form.Height))
  $bmp.Save($PreviewPath)
  $bmp.Dispose()
 }
 $form.ClientSize=New-Object Drawing.Size 260,700
 [Windows.Forms.Application]::DoEvents()
 $footer=$panel.Controls[0].GetControlFromPosition(0,1)
 if($footer.Bottom -gt $panel.ClientSize.Height -or $footer.Controls.Count -ne 3) { throw 'Footer clipped' }
 if($groups.Bottom -gt $footer.Top) { throw 'Commands overlap footer' }
 if($footer.Height -lt 30 -or -not $footer.Visible) { throw 'Footer not visible' }
 Write-Host 'Command panel checks passed: four-row positions, category backgrounds, text width, rebuild.'
} finally { $form.Dispose() }
