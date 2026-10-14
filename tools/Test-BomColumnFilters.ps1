param([string]$PreviewPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
$repo=Split-Path -Parent $PSScriptRoot
$a=[Reflection.Assembly]::LoadFrom("$repo\tools\MechKitHarness\bin\Release\MechKit.dll")
$h=[Reflection.Assembly]::LoadFrom("$repo\tools\MechKitHarness\bin\Release\MechKitHarness.exe")
$t=$a.GetType('MechKit.UI.PartListForm',$true)
$hostObject=[Activator]::CreateInstance($h.GetType('MechKit.Harness.FakeHost'),$true)
$f=[Activator]::CreateInstance($t,@($hostObject))
$flags=[Reflection.BindingFlags]'NonPublic,Instance'
function Check($ok,$message){if(-not $ok){throw $message}}
function RefreshRows {$t.GetMethod('ShowFilteredRows',$flags).Invoke($f,@($false))|Out-Null}
try {
 $f.Opacity=0;$f.ShowInTaskbar=$false;$f.Show();[Windows.Forms.Application]::DoEvents()
 $t.GetMethod('RunSummary',$flags).Invoke($f,@())|Out-Null
 $rows=$t.GetField('_rows',$flags).GetValue($f)
 $grid=$t.GetField('_grid',$flags).GetValue($f)
 $filters=$t.GetField('_columnFilters',$flags).GetValue($f)
 $allowed=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 [void]$allowed.Add($rows[1].Classification)
 $filters.Add('classification',$allowed);RefreshRows
 Check ($grid.Rows.Count -eq 2) 'Checked category did not limit rows'
 $grid.Rows[0].Cells['select'].Value=$true
 $marked=$grid.Rows[0].Tag
 $marked.PurchaseLink='https://example.com/FilterTarget'
 $purchase=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 [void]$purchase.Add($marked.PurchaseLink)
 $filters.Add('purchaselink',$purchase);RefreshRows
 Check ($grid.Rows.Count -eq 1 -and $grid.Rows[0].Tag -eq $marked) 'Multiple column filters did not intersect'
 Check ($grid.Rows[0].Cells['select'].Value) 'Column filter lost export mark'
 $filters['purchaselink']=[Collections.Generic.HashSet[string]]::new()
 RefreshRows
 Check ($grid.Rows.Count -eq 0) 'Unchecked all values should show zero rows'
 $filters.Clear();RefreshRows
 Check (@($grid.Rows|Where-Object {$null -ne $_.Tag}).Count -eq 3) 'Clearing column filters did not restore rows'
 $pType=$a.GetType('MechKit.UI.BomColumnFilterForm',$true)
 $selected=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 [void]$selected.Add('6061')
 $popup=[Activator]::CreateInstance($pType,[Reflection.BindingFlags]'NonPublic,Instance',$null,@('Material',[string[]]@('6061','304','','6061'),$selected),$null)
 try {
  $list=$pType.GetField('_values',$flags).GetValue($popup)
  Check ($list.Items.Count -eq 3 -and $list.CheckedItems.Count -eq 1) 'Popup uniqueness or previous choices failed'
  $popup.Show();[Windows.Forms.Application]::DoEvents()
  if($PreviewPath){
   $bitmap=New-Object Drawing.Bitmap $popup.Width,$popup.Height
   try{$popup.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle 0,0,$popup.Width,$popup.Height));$bitmap.Save([IO.Path]::GetFullPath($PreviewPath))}finally{$bitmap.Dispose()}
  }
  $popup.AcceptButton.PerformClick()
  $saved=$pType.GetProperty('SelectedValues',$flags).GetValue($popup,$null)
  Check ($saved.Count -eq 1 -and $saved.Contains('6061')) 'Popup did not save checked values'
 }finally{$popup.Dispose()}
 Write-Output 'PASS: checked categories, column intersection, empty selection, clear, export marks, popup choices and confirmation'
}finally{$f.Dispose()}
