$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
$repo=Split-Path -Parent $PSScriptRoot
$a=[Reflection.Assembly]::LoadFrom("$repo\tools\MechKitHarness\bin\Release\MechKit.dll")
$h=[Reflection.Assembly]::LoadFrom("$repo\tools\MechKitHarness\bin\Release\MechKitHarness.exe")
$hostObject=[Activator]::CreateInstance($h.GetType('MechKit.Harness.FakeHost'),$true)
$t=$a.GetType('MechKit.UI.PartListForm',$true)
$f=[Activator]::CreateInstance($t,@($hostObject))
$flags=[Reflection.BindingFlags]'NonPublic,Instance'
function Check($ok,$message){if(-not $ok){throw $message}}
function WaitSearch {
 $deadline=[DateTime]::UtcNow.AddMilliseconds(350)
 while([DateTime]::UtcNow -lt $deadline){[Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 10}
}
try {
 $f.ShowInTaskbar=$false;$f.Opacity=0;$f.Show();[Windows.Forms.Application]::DoEvents()
 $t.GetMethod('RunSummary',$flags).Invoke($f,@())|Out-Null
 $rows=$t.GetField('_rows',$flags).GetValue($f)
 $grid=$t.GetField('_grid',$flags).GetValue($f)
 $search=$t.GetField('_keywordSearch',$flags).GetValue($f)
 $filter=$t.GetField('_classificationFilter',$flags).GetValue($f)
 $rows[0].FullName='Widget Alpha';$rows[0].Name='Widget Alpha';$rows[0].Material='6061'
 $rows[0].Location='Root > Hidden subassembly';$rows[0].PurchaseLink='https://example.com/UniqueToken'
 $t.GetMethod('ShowFilteredRows',$flags).Invoke($f,@($false))|Out-Null
 $search.Text='ALPHA 6061';WaitSearch
 $visible=@($grid.Rows | Where-Object {$null -ne $_.Tag})
 Check ($visible.Count -eq 1 -and $visible[0].Tag -eq $rows[0]) 'Combined case-insensitive keyword matching failed'
 Check ($grid.Rows.Count -eq 1) 'Search kept unrelated tree spacer rows'
 $visible[0].Cells['select'].Value=$true
 $visible[0].Cells['material'].Value='7075'
 $search.Text='uniqueTOKEN';WaitSearch
 Check ($rows[0].Material -eq '7075') 'Search discarded unsaved material edit'
 Check ($grid.Rows[0].Cells['select'].Value) 'Search discarded export selection'
 $filter.SelectedIndex=2
 Check ($grid.Rows.Count -eq 0) 'Search did not combine with type filter'
 $filter.SelectedIndex=0
 Check ($grid.Rows.Count -eq 1) 'Type filter did not restore search results'
 $search.Text='no_such_keyword';WaitSearch
 Check ($grid.Rows.Count -eq 0) 'No-match search showed rows'
 $search.Clear();WaitSearch
 $visible=@($grid.Rows | Where-Object {$null -ne $_.Tag})
 Check ($visible.Count -eq 3) 'Clearing search did not restore original rows'
 $target=@($visible | Where-Object {$_.Tag -eq $rows[0]})[0]
 Check ($target.Cells['select'].Value -and $target.Cells['material'].Value -eq '7075') 'Clearing search lost edits or selection'
 Write-Output 'PASS: multi-keyword matching, case, links, no spacer rows, type filter combination, empty results, clear, edits and export marks preserved'
}finally{$f.Dispose()}
