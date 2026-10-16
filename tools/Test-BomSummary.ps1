$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Windows.Forms
$repo = Split-Path -Parent $PSScriptRoot
$a = [Reflection.Assembly]::LoadFrom("$repo\src\MechKit\bin\Release\MechKit.dll")
$rowType = $a.GetType('MechKit.Features.PartListRow', $true)
$service = $a.GetType('MechKit.Features.PartListService', $true)
$rows = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($rowType))
function Check($ok, $message) { if (-not $ok) { throw $message } }
function Summary($items) { return $service.GetMethod('BuildSummary').Invoke($null, (, $items)) }
foreach ($spec in @(
    @('标准件', 'C:\parts\bolt.sldprt', 'Default', 120),
    @('标准件', 'c:/parts/BOLT.sldprt', 'default', 80),
    @('加工件', 'C:\parts\plate.sldprt', 'Default', 100),
    @('加工件', 'C:\parts\plate.sldprt', 'Other', 51),
    @('外部图纸', 'C:\parts\drawing.sldprt', 'Default', 26)
)) {
    $row = [Activator]::CreateInstance($rowType)
    $row.Classification = $spec[0]; $row.FilePath = $spec[1]
    $row.Configuration = $spec[2]; $row.Quantity = $spec[3]
    $rows.Add($row)
}
$expected = '标准件 1 种 / 200 件；加工件 2 种 / 151 件；外部图纸 1 种 / 26 件；合计：4 种 / 377 件。'
Check ((Summary $rows) -eq $expected) 'Category kinds or quantities incorrect'
$merged = $service.GetMethod('MergeForExport').Invoke($null, (, $rows))
Check ((Summary $merged) -eq $expected) 'Summary changed after merged export'
$empty = [Activator]::CreateInstance($rows.GetType())
Check ((Summary $empty) -eq '合计：0 种 / 0 件。') 'Empty summary incorrect'
foreach ($ignored in 1..2) {
    $row = [Activator]::CreateInstance($rowType)
    $row.Name = 'same unsaved name'; $row.Quantity = [int]::MaxValue
    $empty.Add($row)
}
Check ((Summary $empty).Contains('合计：2 种 / 4294967294 件')) 'Pathless parts merged or total overflowed'

[string]$folder = Join-Path $repo 'artifacts\bom-summary-test'
[IO.Directory]::CreateDirectory($folder) | Out-Null
$options = [Activator]::CreateInstance($a.GetType('MechKit.Features.PartListOptions'))
$csv = $service.GetMethod('ExportCsv').Invoke($null, @($rows, $folder, 'summary', $options))
$lastLine = [IO.File]::ReadAllLines($csv) | Select-Object -Last 1
Check ($lastLine.Contains($expected)) 'CSV last line missing summary'
$xlsx = $a.GetType('MechKit.Features.BomExcelExporter').GetMethod('Export').Invoke($null, @($null, $rows, $folder, 'summary', $options))
$zip = [IO.Compression.ZipFile]::OpenRead($xlsx)
try {
    $reader = New-Object IO.StreamReader ($zip.GetEntry('xl/worksheets/sheet1.xml').Open())
    try { [xml]$sheet = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $ns = New-Object Xml.XmlNamespaceManager $sheet.NameTable
    $ns.AddNamespace('s', 'http://schemas.openxmlformats.org/spreadsheetml/2006/main')
    $last = $sheet.SelectSingleNode('//s:sheetData/s:row[last()]', $ns)
    Check ($last.SelectSingleNode('s:c/s:is/s:t', $ns).InnerText -eq $expected) 'XLSX last row missing summary'
    $merge = $sheet.SelectSingleNode('//s:mergeCells/s:mergeCell', $ns).GetAttribute('ref')
    Check ($merge -eq 'A8:N8') 'XLSX summary does not span all export columns'
} finally { $zip.Dispose() }

$h = [Reflection.Assembly]::LoadFrom("$repo\tools\MechKitHarness\bin\Release\MechKitHarness.exe")
$hostObject = [Activator]::CreateInstance($h.GetType('MechKit.Harness.FakeHost'), $true)
$formType = $a.GetType('MechKit.UI.PartListForm')
$f = [Activator]::CreateInstance($formType, @($hostObject))
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
try {
    $f.ShowInTaskbar = $false; $f.Opacity = 0; $f.Show()
    [Windows.Forms.Application]::DoEvents()
    $formType.GetMethod('RunSummary', $flags).Invoke($f, @()) | Out-Null
    $label = $formType.GetField('_summaryLabel', $flags).GetValue($f)
    $status = $formType.GetField('_status', $flags).GetValue($f)
    $search = $formType.GetField('_keywordSearch', $flags).GetValue($f)
    $preview = $formType.GetField('_rows', $flags).GetValue($f)
    $grid = $formType.GetField('_grid', $flags).GetValue($f)
    Check ($label.Text -eq (Summary $preview)) 'UI summary differs from export'
    $savedSummary = $label.Text; $status.Text = '导入完成'
    Check ($label.Text -eq $savedSummary) 'Status replaced persistent summary'
    $search.Text = 'not_present_anywhere'
    $formType.GetMethod('ShowFilteredRows', $flags).Invoke($f, @($false)) | Out-Null
    Check ($label.Text -eq '合计：0 种 / 0 件。') 'Filtered empty summary stale'
    $search.Clear()
    $formType.GetMethod('ShowFilteredRows', $flags).Invoke($f, @($false)) | Out-Null
    Check ($label.Text -eq $savedSummary) 'Clearing filter did not restore totals'
    $f.ClientSize = New-Object Drawing.Size 1080, 640
    $f.PerformLayout()
    Check ($label.Top -ge $grid.Bottom -and $label.Bottom -le $label.Parent.ClientSize.Height) 'Summary overlaps grid or is clipped'
    $bitmap = New-Object Drawing.Bitmap $f.Width, $f.Height
    try {
        $f.DrawToBitmap($bitmap, (New-Object Drawing.Rectangle 0, 0, $f.Width, $f.Height))
        $bitmap.Save((Join-Path $folder 'summary-preview.png'), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
} finally { $f.Dispose() }
Write-Host 'PASS: category/overall totals, unique file/configuration kinds, merged exports, pathless parts, large totals, CSV/XLSX footer and persistent filtered UI summary.'
