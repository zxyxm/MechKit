$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
$root=Split-Path -Parent $PSScriptRoot
$h=[Reflection.Assembly]::LoadFrom((Join-Path $root 'tools\MechKitHarness\bin\Release\MechKitHarness.exe'))
$a=[Reflection.Assembly]::LoadFrom((Join-Path $root 'tools\MechKitHarness\bin\Release\MechKit.dll'))
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$layout=$a.GetType('MechKit.UI.WindowLayout').GetMethod('BomWorkspace',[Reflection.BindingFlags]'Static,NonPublic')
foreach($origin in @(0,-1920,1920)) {
 $hostRect=New-Object Drawing.Rectangle $origin,0,1920,1080
 $graphics=New-Object Drawing.Rectangle ($origin+300),160,1250,850
 $r=$layout.Invoke($null,@($hostRect,$graphics,$hostRect))
 if(-not $graphics.Contains($r) -or $r.Width -ne 1238){throw 'Workspace covers side panes or uses wrong monitor'}
 $fallback=$layout.Invoke($null,@($hostRect,[Drawing.Rectangle]::Empty,$hostRect))
 if($fallback.Left -le $hostRect.Left -or $fallback.Right -ge $hostRect.Right){throw 'Fallback did not reserve sidebars'}
}
$hostObject=[Activator]::CreateInstance($h.GetType('MechKit.Harness.FakeHost'),$true)
$t=$a.GetType('MechKit.UI.PartListForm')
$form=[Activator]::CreateInstance($t,@($hostObject))
try {
 $form.ShowInTaskbar=$false;$form.Opacity=0;$form.Show()
 [Windows.Forms.Application]::DoEvents()
 $rows=$t.GetField('_rows',$flags).GetValue($form)
 $grid=$t.GetField('_grid',$flags).GetValue($form)
 $rows[0].FilePath='C:\test\first.SLDPRT'
 $rows[1].FilePath='C:\test\second.SLDPRT'
 $original=$rows[0].FullName;$other=$rows[1].FullName
 $grid.ClearSelection()
 foreach($r in $grid.Rows){if([object]::ReferenceEquals($r.Tag,$rows[0])){$r.Cells['fullname'].Selected=$true}}
 $preview=$t.GetMethod('PreviewQuickNaming',$flags)
 $change=[Func[string,string]]{param($name) 'prefix-'+$name}
 if(-not $preview.Invoke($form,@($change,'test'))){throw 'Selected BOM row not handled'}
 if($rows[0].FullName -ne ('prefix-'+$original) -or $rows[1].FullName -ne $other){throw 'Wrong rows changed'}
 if($rows[0].FilePath -ne 'C:\test\first.SLDPRT'){throw 'Preview changed file path'}
 $preview.Invoke($form,@($change,'test'))|Out-Null
 if($rows[0].FullName -ne ('prefix-prefix-'+$original)){throw 'Selection or consecutive preview lost'}
 if(-not $t.GetMethod('UndoQuickNamingPreview',$flags).Invoke($form,@())){throw 'Preview undo failed'}
 if($rows[0].FullName -ne ('prefix-'+$original)){throw 'Undo did not restore previous preview'}
 Write-Host 'PASS: three-panel bounds on three monitor origins, selected-row preview, consecutive commands, source path preservation and undo.'
} finally {$form.Dispose()}
