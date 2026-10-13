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
 $groups=$panel.Controls[0]
 if($groups.Controls.Count -ne 8) { throw 'Category count' }
 foreach($section in $groups.Controls) {
  $grid=$section.GetControlFromPosition(0,1)
  if($grid.RowCount -gt 4) { throw 'More than four rows' }
  for($i=0;$i -lt $grid.Controls.Count;$i++) {
   $button=$grid.GetControlFromPosition([Math]::Floor($i/4),($i%4))
   if($null -eq $button -or $button.BackColor -ne $section.BackColor) { throw 'Position or category background mismatch' }
   if($button.Height -lt 32 -or $button.Width -lt $button.PreferredSize.Width) { throw 'Clipped button' }
  }
 }
 $t.GetMethod('Rebuild',[Reflection.BindingFlags]'Instance,NonPublic').Invoke($panel,@())
 if($groups.Controls.Count -ne 8) { throw 'Rebuild duplicated categories' }
 [Windows.Forms.Application]::DoEvents()
 if($PreviewPath) {
  $bmp=New-Object Drawing.Bitmap 660,900
  $form.DrawToBitmap($bmp,(New-Object Drawing.Rectangle 0,0,660,900))
  $bmp.Save($PreviewPath)
  $bmp.Dispose()
 }
 $form.ClientSize=New-Object Drawing.Size 320,700
 [Windows.Forms.Application]::DoEvents()
 if(-not $groups.HorizontalScroll.Visible) { throw 'Narrow pane must expose horizontal scrolling' }
 Write-Host 'Command panel checks passed: four-row positions, category backgrounds, text width, rebuild.'
} finally { $form.Dispose() }
