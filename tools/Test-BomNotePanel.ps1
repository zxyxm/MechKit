$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
$repo=Split-Path -Parent $PSScriptRoot
$a=[Reflection.Assembly]::LoadFrom("$repo\tools\MechKitHarness\bin\Release\MechKit.dll")
$h=[Reflection.Assembly]::LoadFrom("$repo\tools\MechKitHarness\bin\Release\MechKitHarness.exe")
$flags=[Reflection.BindingFlags]'NonPublic,Instance'
$t=$a.GetType('MechKit.UI.PartListForm');$pt=$a.GetType('MechKit.UI.BomNotePanel')
$hostObject=[Activator]::CreateInstance($h.GetType('MechKit.Harness.FakeHost'),$true)
$f=[Activator]::CreateInstance($t,@($hostObject));$p=[Activator]::CreateInstance($pt,$true)
try {
 $f.ShowInTaskbar=$false;$f.Show();[Windows.Forms.Application]::DoEvents()
 $t.GetMethod('RunSummary',$flags).Invoke($f,@())|Out-Null
 $g=$t.GetField('_grid',$flags).GetValue($f);$display=@($g.Rows|Where-Object {$null -ne $_.Tag})[0];$row=$display.Tag
 $root=Join-Path $repo ('artifacts\note-panel-'+[Guid]::NewGuid().ToString('N'));[IO.Directory]::CreateDirectory($root)|Out-Null
 $row.FilePath=Join-Path $root 'part.sldprt';[IO.File]::WriteAllText($row.FilePath,'fixture')
 $pt.GetMethod('Bind',$flags).Invoke($p,@($row))|Out-Null
 $editorHost=[Windows.Forms.Form]::new();$editorHost.ShowInTaskbar=$false;$editorHost.Controls.Add($p);$editorHost.Show()
 $editGrid=$pt.GetField('_fields',$flags).GetValue($p)
 foreach($column in @(0,1)) {
  $pt.GetMethod('BeginFieldEdit',$flags).Invoke($p,@($editGrid,[Windows.Forms.DataGridViewCellEventArgs]::new($column,0)))|Out-Null
  if(!$editGrid.IsCurrentCellInEditMode -or $editGrid.CurrentCell.ColumnIndex -ne 1){throw 'Double click did not edit purchase link'}
  $editGrid.EndEdit()|Out-Null
 }
 $link=$pt.GetField('_fields',$flags).GetValue($p).Rows[0].Cells[1];$remark=$pt.GetField('_fields',$flags).GetValue($p).Rows[1].Cells[1]
 $link.Value='https://example.com/part';$remark.Value='panel note'
 $fields=$pt.GetField('_fields',$flags).GetValue($p);$fields.Rows[2].Cells[1].Value='assembly instructions';$fields.Rows[3].Cells[1].Value='pending matters'
 $pt.GetMethod('Save',$flags).Invoke($p,@())|Out-Null
 if($row.Remark -ne 'panel note' -or $display.Cells['remark'].Value -ne 'panel note' -or $row.PurchaseLink -ne $link.Value){throw 'Panel to BOM synchronization failed'}
 if($row.AssemblyNote -ne 'assembly instructions' -or $row.Unfinished -ne 'pending matters'){throw 'Extra manual fields not saved'}
 $store=$a.GetType('MechKit.Core.BomNoteStore');$save=$store.GetMethod('Save',[Reflection.BindingFlags]'NonPublic,Static')
 $save.Invoke($null,@([string]$row.FilePath,'remark','external note'))|Out-Null
 if($remark.Value -ne 'external note' -or $display.Cells['remark'].Value -ne 'external note'){throw 'Asset to panel/BOM update failed'}
 $link.Value='javascript:alert(1)';$pt.GetMethod('Save',$flags).Invoke($p,@())|Out-Null
 if($row.PurchaseLink -ne 'https://example.com/part'){throw 'Invalid URL persisted'}
 $link.Value='https://example.com/part'
 $automatic=[Activator]::CreateInstance($a.GetType('MechKit.Features.PartListRow'))
 $automatic.FilePath=Join-Path $root 'agent.sldprt';[IO.File]::WriteAllText($automatic.FilePath,'fixture')
 $automatic.PurchaseLink='代理';$automatic.Remark='自动备注'
 $store.GetMethod('Apply',[Reflection.BindingFlags]'NonPublic,Static').Invoke($null,@($automatic))|Out-Null
 $pt.GetMethod('Bind',$flags).Invoke($p,@($automatic))|Out-Null
 if($fields.Rows[0].Cells[1].ReadOnly){throw 'Agent placeholder locked'}
 if(!$fields.Rows[1].Cells[1].ReadOnly -or !$fields.Rows[1].Cells[0].Value.StartsWith('🔒 ')){throw 'Automatic remark not marked and locked'}
 if($fields.Rows[2].Cells[1].ReadOnly){throw 'Empty manual field locked'}
 $pt.GetMethod('BeginFieldEdit',$flags).Invoke($p,@($fields,[Windows.Forms.DataGridViewCellEventArgs]::new(1,0)))|Out-Null
 if(!$fields.IsCurrentCellInEditMode){throw 'Agent link cannot enter editor'}
 $fields.EditingControl.Text='https://example.com/agent';$fields.EndEdit()|Out-Null
 $pt.GetMethod('Bind',$flags).Invoke($p,@($automatic))|Out-Null
 $save.Invoke($null,@([string]$automatic.FilePath,'remark','manual override'))|Out-Null
 if($fields.Rows[1].Cells[1].ReadOnly){throw 'Manual value remained locked'}
 $pt.GetMethod('Bind',$flags).Invoke($p,[object[]]@($null))|Out-Null
 if(!$fields.Rows[0].Cells[1].ReadOnly){throw 'Unbound editor enabled'}
 Write-Output 'PASS: panel saves to assets and live BOM, reverse update, invalid URL rejection, unbound state.'
}finally{$p.Dispose();if($editorHost){$editorHost.Dispose()};$f.Dispose()}
