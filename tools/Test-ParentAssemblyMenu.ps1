$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
$repo=Split-Path -Parent $PSScriptRoot
$a=[Reflection.Assembly]::LoadFrom("$repo\tools\MechKitHarness\bin\Release\MechKit.dll")
$h=[Reflection.Assembly]::LoadFrom("$repo\tools\MechKitHarness\bin\Release\MechKitHarness.exe")
$t=$a.GetType('MechKit.UI.PartListForm',$true)
$flags=[Reflection.BindingFlags]'NonPublic,Instance'
$hostObject=[Activator]::CreateInstance($h.GetType('MechKit.Harness.FakeHost'),$true)
$form=[Activator]::CreateInstance($t,@($hostObject))
try {
 $form.ShowInTaskbar=$false;$form.Show();[Windows.Forms.Application]::DoEvents()
 $t.GetMethod('RunSummary',$flags).Invoke($form,@())|Out-Null
 $g=$t.GetField('_grid',$flags).GetValue($form)
 $rows=@($g.Rows|Where-Object {$null -ne $_.Tag})
 $row=$rows[0];$other=$rows[1]
 $row.Tag.FilePath='C:\fixture\current.sldprt'
 $other.Tag.FilePath='C:\fixture\other.sldprt'
 $parent=[Activator]::CreateInstance($a.GetType('MechKit.Features.PartListRow'));$parent.FilePath='C:\fixture\parent.sldasm'
 $grand=[Activator]::CreateInstance($a.GetType('MechKit.Features.PartListRow'));$grand.FilePath='C:\fixture\grand.sldasm'
 $row.Tag.ParentAssemblies.Add($parent);$row.Tag.ParentAssemblies.Add($grand)
 $fire=[Windows.Forms.DataGridView].GetMethod('OnCellMouseDown',$flags,$null,[Type[]]@([Windows.Forms.DataGridViewCellMouseEventArgs]),$null)
 function RightClick($r,$column) {
  $e=[Windows.Forms.DataGridViewCellMouseEventArgs]::new($g.Columns[$column].Index,$r.Index,2,2,[Windows.Forms.MouseEventArgs]::new([Windows.Forms.MouseButtons]::Right,1,2,2,0))
  $fire.Invoke($g,@($e))|Out-Null
 }
 RightClick $row 'location'
 $menu=$g.ContextMenuStrip
 if($menu.Items.Count -ne 8 -or !$menu.Items[0].Enabled -or $menu.Items[0].Text -ne '打开当前零件' -or $menu.Items[0].ToolTipText -ne $row.Tag.FilePath -or !$menu.Items[1].Enabled -or !$menu.Items[2].Enabled -or $menu.Items[1].ToolTipText -ne $parent.FilePath -or $menu.Items[2].ToolTipText -ne $grand.FilePath){throw 'Parent entries missing from shared menu'}
 RightClick $row 'location'
 if($menu.Items.Count -ne 8){throw 'Menu duplicated entries'}
 RightClick $other 'location'
 if($menu.Items[0].ToolTipText -ne $other.Tag.FilePath -or $menu.Items[1].Enabled -or $menu.Items[2].Enabled){throw 'Previous row target leaked'}
 RightClick $row 'remark'
 if($menu.Items.Count -ne 4){throw 'Parent commands leaked to non-location column'}
 Write-Output 'PASS: actual cell mouse event, shared menu, current part and two parent targets, repeat click, absent parents, ordinary columns.'
} finally {$form.Dispose()}
