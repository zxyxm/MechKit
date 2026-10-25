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
try{
 $f.ShowInTaskbar=$false;$f.Show();$f.Activate();[Windows.Forms.Application]::DoEvents()
 $t.GetMethod('RunSummary',$flags).Invoke($f,@())|Out-Null
 $grid=$t.GetField('_grid',$flags).GetValue($f)
 $row=@($grid.Rows | Where-Object {$null -ne $_.Tag})[0]
 $layout=$a.GetType('MechKit.UI.WindowLayout',$true)
 $shortcut=$layout.GetMethod('HandleWindowShortcut',[Reflection.BindingFlags]'NonPublic,Static')
 foreach($key in @('assemblynote','remark','purchaselink')){
  $cell=$row.Cells[$key]
  Check (-not $cell.ReadOnly -and $cell -is [Windows.Forms.DataGridViewTextBoxCell]) 'Property cell is not editable text'
  $beforeForms=[Windows.Forms.Application]::OpenForms.Count
  $e=New-Object Windows.Forms.DataGridViewCellEventArgs $cell.ColumnIndex,$row.Index
  $arguments=New-Object object[] 2
  $arguments[0]=$grid;$arguments[1]=$e.PSObject.BaseObject
  $t.GetMethod('OnGridCellDoubleClick',$flags).Invoke($f,$arguments)|Out-Null
  Check ($grid.IsCurrentCellInEditMode -and $grid.EditingControl -is [Windows.Forms.TextBox]) 'Double click did not begin inline edit'
  Check ($grid.SelectedCells.Count -eq 1) 'Editing unexpectedly selected full row'
  Check ([Windows.Forms.Application]::OpenForms.Count -eq $beforeForms) 'Double click opened another window'
  $grid.EditingControl.Text='Temporary edit'
  Check (-not $shortcut.Invoke($null,@([Windows.Forms.Keys]::Tab))) 'Tab was stolen from cell editor'
  $grid.EditingControl.Focus()|Out-Null
  [Windows.Forms.Application]::DoEvents()
  Check ($shortcut.Invoke($null,@([Windows.Forms.Keys]::Escape))) 'Escape did not cancel inline edit'
  [Windows.Forms.Application]::DoEvents()
  Check (-not $grid.IsCurrentCellInEditMode) 'Escape left cell editor open'
  Check (-not $f.IsDisposed) 'Cancel edit closed BOM'
 }
 $cell=$row.Cells['purchaselink']
 $grid.CurrentCell=$cell;$grid.BeginEdit($true)|Out-Null
 $grid.EditingControl.Text='https://example.com/test'
 $beforeForms=[Windows.Forms.Application]::OpenForms.Count
 $grid.EndEdit()|Out-Null
 Check (-not [string]::IsNullOrEmpty($cell.ErrorText)) 'Unavailable SW save failure was not reported'
 Check ([Windows.Forms.Application]::OpenForms.Count -eq $beforeForms) 'Save failure opened a dialog'
 Write-Output 'PASS: three inline editors, cell selection, no dialog, Tab navigation, cancel and inline save failure'
}finally{$f.Dispose()}
