$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
$repo=Split-Path -Parent $PSScriptRoot
$a=[Reflection.Assembly]::LoadFrom("$repo\tools\MechKitHarness\bin\Release\MechKit.dll")
$h=[Reflection.Assembly]::LoadFrom("$repo\tools\MechKitHarness\bin\Release\MechKitHarness.exe")
$t=$a.GetType('MechKit.UI.PartListForm',$true)
$flags=[Reflection.BindingFlags]'NonPublic,Instance'
$hostObject=[Activator]::CreateInstance($h.GetType('MechKit.Harness.FakeHost'),$true)
$f=[Activator]::CreateInstance($t,@($hostObject))
function Check($ok,$message){if(-not $ok){throw $message}}
try {
 $f.ShowInTaskbar=$false;$f.Show();[Windows.Forms.Application]::DoEvents()
 $t.GetMethod('RunSummary',$flags).Invoke($f,@())|Out-Null
 $g=$t.GetField('_grid',$flags).GetValue($f)
 $rowCount=$g.Rows.Count
 $rows=@($g.Rows|Where-Object {$null -ne $_.Tag})
 $fixture=Join-Path $repo ('artifacts\note-grid-'+[Guid]::NewGuid().ToString('N'));[IO.Directory]::CreateDirectory($fixture)|Out-Null
 foreach($item in $rows){$item.Tag.FilePath=Join-Path $fixture ('test-'+$item.Index+'.sldprt');[IO.File]::WriteAllText($item.Tag.FilePath,'fixture')}
 Check (-not $g.Columns.Contains('designnote')) 'Design note column still visible'
 Check ($g.Columns.Contains('unfinished') -and -not $g.Columns['unfinished'].ReadOnly) 'Missing editable unfinished field'
 $r=$rows[0];$other=@($rows|Where-Object {$_.Tag.FilePath -ne $r.Tag.FilePath})[0]
 $apply=$t.GetMethod('ApplySheetText',$flags)
 $g.CurrentCell=$r.Cells['remark'];$g.ClearSelection()
 $r.Cells['remark'].Selected=$true;$other.Cells['remark'].Selected=$true
 $r.Cells['quantity'].Selected=$true
 $qty=$r.Cells['quantity'].Value
 $apply.Invoke($f,@('Test note'))|Out-Null
 Check ($r.Tag.Remark -eq 'Test note' -and $other.Tag.Remark -eq 'Test note') 'Range fill failed'
 Check ($r.Cells['quantity'].Value -eq $qty) 'Read-only quantity changed'
 Check (-not $r.Tag.HasBomEdits) 'Saved asset edit left pending changes'
 $args=New-Object object[] 1
 $apply.Invoke($f,$args)|Out-Null
 Check ($r.Tag.Remark -eq '' -and $other.Tag.Remark -eq '') 'Range clear failed'
 Check ($g.Rows.Count -eq $rowCount) 'Clear deleted rows'
 $g.CurrentCell=$r.Cells['material'];$g.ClearSelection();$g.CurrentCell.Selected=$true
 $oldMaterial=$r.Tag.Material;$oldProcess=$r.Tag.Process
 $apply.Invoke($f,@("Steel`tMachining"))|Out-Null
 Check ($r.Tag.Material -eq $oldMaterial -and $r.Tag.Process -eq $oldProcess) 'Generated columns changed by paste'
 foreach($key in @('name','quantity','location','classification','material','process','surface','drawing','sequence')) {
  Check ($g.Columns[$key].ReadOnly) ('Not locked: '+$key)
  Check ($g.Columns[$key].HeaderText.StartsWith('🔒 ')) ('Missing lock: '+$key)
  $g.CurrentCell=$r.Cells[$key]
  Check (-not $g.BeginEdit($true)) ('Edit allowed: '+$key)
 }
 Check (-not $g.Columns['fullname'].ReadOnly -and -not $g.Columns['fullname'].HeaderText.StartsWith('🔒 ')) '3D filename not editable'
 $g.CurrentCell=$r.Cells['remark'];$g.ClearSelection();$g.CurrentCell.Selected=$true
 $rIndex=$r.Index
 $next=$g.Rows[$rIndex+1]
 $oldPath=$next.Tag.FilePath;$next.Tag.FilePath=$r.Tag.FilePath
 $failed=$false
 try {$apply.Invoke($f,@("first`nsecond"))|Out-Null}catch{$failed=$true}
 Check ($failed -and $r.Tag.Remark -eq '') 'Conflicting duplicate values were partially applied'
 $apply.Invoke($f,@('Shared'))|Out-Null
 Check ($next.Tag.Remark -eq 'Shared' -and $next.Cells['remark'].Value -eq 'Shared') 'Duplicate file not synchronized'
 $next.Tag.FilePath=$oldPath
 $parse=$t.GetMethod('ParseSheetText',[Reflection.BindingFlags]'NonPublic,Static')
 $parsed=$parse.Invoke($null,@("`"a`tb`"`t`"line1`nline2`"`r`n"))
 Check ($parsed.Count -eq 1 -and $parsed[0][0] -eq "a`tb" -and $parsed[0][1] -eq "line1`nline2") 'Quoted TSV failed'
 $g.CurrentCell=$r.Cells['remark'];$g.ClearSelection();$g.CurrentCell.Selected=$true
 $g.BeginEdit($true)|Out-Null
 Check ($g.SelectedCells.Count -eq 1) 'Edit expanded selection to entire row'
 $g.CancelEdit()|Out-Null
 $doubleClick=$t.GetMethod('OnGridCellDoubleClick',$flags)
 foreach($key in @('remark','fullname')) {
  $doubleClick.Invoke($f,@($g,[Windows.Forms.DataGridViewCellEventArgs]::new($g.Columns[$key].Index,$r.Index)))|Out-Null
  Check ($g.IsCurrentCellInEditMode -and $g.CurrentCell.OwningColumn.Name -eq $key) ('Double click did not edit: '+$key)
  $g.CancelEdit()|Out-Null
 }
 [Windows.Forms.Clipboard]::SetText('double-click-sentinel')
 foreach($key in @('location','drawing','quantity','designTree')) {
  $g.CurrentCell=$r.Cells[$key]
  $doubleClick.Invoke($f,@($g,[Windows.Forms.DataGridViewCellEventArgs]::new($g.Columns[$key].Index,$r.Index)))|Out-Null
  Check (-not $g.IsCurrentCellInEditMode) ('Double click edited locked column: '+$key)
 }
 Check ([Windows.Forms.Clipboard]::GetText() -eq 'double-click-sentinel') 'Double click copied a locked cell'
 Write-Output 'PASS: range fill/clear, protected quantity, staged writes, rectangle paste, quoted TSV, cell selection'
} finally {$f.Dispose()}
