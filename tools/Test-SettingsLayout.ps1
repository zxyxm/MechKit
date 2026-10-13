$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
$root=Split-Path -Parent $PSScriptRoot
$a=[Reflection.Assembly]::LoadFrom("$root\tools\MechKitHarness\bin\Release\MechKit.dll")
$h=[Reflection.Assembly]::LoadFrom("$root\tools\MechKitHarness\bin\Release\MechKitHarness.exe")
$hostObject=[Activator]::CreateInstance($h.GetType('MechKit.Harness.FakeHost'),$true)
$t=$a.GetType('MechKit.UI.SettingsForm');$f=[Activator]::CreateInstance($t,@($hostObject))
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
try {
 $table=$t.GetField('_mappingTable',$flags).GetValue($f)
 $order=$t.GetField('_columnOrder',$flags).GetValue($f)
 if($table.ColumnCount -ne 3 -or $table.RowCount -ne ($order.Count+1)) {throw 'Field count mismatch'}
 for($r=0;$r -lt $table.RowCount;$r++) {for($c=0;$c -lt 3;$c++){if($null -eq $table.GetControlFromPosition($c,$r)){throw 'Missing field control'}}}
 $editor=$table.GetControlFromPosition(0,1);$editor.Text='Custom header'
 $selector=$t.GetField('_columnOrderSelector',$flags).GetValue($f);$selector.SelectedIndex=0
 $t.GetMethod('MoveSelectedColumn',$flags).Invoke($f,@(1))|Out-Null
 if($table.GetControlFromPosition(0,2) -ne $editor -or $editor.Text -ne 'Custom header') {throw 'Reorder lost header editing'}
 Write-Host 'Settings mapping coverage and reorder checks passed.'
} finally {$f.Dispose()}
