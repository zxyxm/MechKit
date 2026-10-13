param([string] $DllPath)
$ErrorActionPreference = 'Stop'
if (-not $DllPath) { $DllPath = Join-Path $PSScriptRoot '..\src\MechKit\bin\Release\MechKit.dll' }
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $DllPath).Path)
$rowType = $assembly.GetType('MechKit.Features.PartListRow', $true)
$service = $assembly.GetType('MechKit.Features.PartListService', $true)
$rows = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($rowType))
foreach ($spec in @(
    @('C:\parts\plate.SLDPRT', 'Default', 2, 'assembly A', 'note A'),
    @('c:/parts/PLATE.sldprt', 'default', 3, 'assembly B', 'note B'),
    @('C:\parts\plate.SLDPRT', 'Other', 4, 'assembly A', ''),
    @('C:\other\plate.SLDPRT', 'Default', 1, '', ''),
    @('', 'Default', 9, '', ''),
    @('', 'Default', 1, '', '')
)) {
    $row = [Activator]::CreateInstance($rowType)
    $row.FilePath = $spec[0]; $row.Configuration = $spec[1]; $row.Quantity = $spec[2]
    $row.Location = $spec[3]; $row.Remark = $spec[4]
    $row.Name = 'plate'; $row.Material = '6061'; $row.Process = 'CNC'
    $rows.Add($row)
}
function Merge($inputRows) { ,$service.GetMethod('MergeForExport').Invoke($null, (, $inputRows)) }
$merged = Merge $rows
if ($merged.Count -ne 5 -or $merged[0].Quantity -ne 5) { throw 'Wrong file/configuration grouping or total' }
if ($merged[1].Quantity -ne 4 -or $merged[2].Quantity -ne 1) { throw 'Different configurations or paths merged' }
if ($merged[3].Quantity -ne 9 -or $merged[4].Quantity -ne 1) { throw 'Pathless rows merged' }
if (-not $merged[0].Location.Contains('assembly A') -or -not $merged[0].Location.Contains('assembly B') -or
    -not $merged[0].Remark.Contains('note A') -or -not $merged[0].Remark.Contains('note B')) { throw 'Per-instance metadata lost' }
if ($merged[0].Material -ne '6061') { throw 'Duplicate field text repeated' }
if ($rows[0].Quantity -ne 2 -or $rows[0].Location -ne 'assembly A' -or $rows.Count -ne 6) { throw 'Preview rows mutated' }
$merged[0].Name = 'edited export'
if ($rows[0].Name -ne 'plate') { throw 'Merged result shares mutable source row' }
$subset = [Activator]::CreateInstance($rows.GetType())
$subset.Add($rows[1])
if ((Merge $subset)[0].Quantity -ne 3) { throw 'Export subset not respected' }
$empty = [Activator]::CreateInstance($rows.GetType())
if ((Merge $empty).Count -ne 0) { throw 'Empty export failed' }
$options = [Activator]::CreateInstance($assembly.GetType('MechKit.Features.PartListOptions', $true))
$options.ColumnOrder = 'quantity,name'
$folder = Join-Path $PSScriptRoot '..\artifacts\bom-merge-test'
[IO.Directory]::CreateDirectory($folder) | Out-Null
$csv = $service.GetMethod('ExportCsv').Invoke($null, @($merged, [string]$folder, 'merged', $options))
$csvRows = @(Import-Csv -LiteralPath $csv -Encoding UTF8 | Where-Object { @($_.PSObject.Properties)[0].Value -match '^\d+$' })
if ($csvRows.Count -ne 5 -or @($csvRows[0].PSObject.Properties)[0].Value -ne '5') { throw 'Merged CSV quantities incorrect' }
$excel = $assembly.GetType('MechKit.Features.BomExcelExporter', $true)
$xlsx = $excel.GetMethod('Export').Invoke($null, @($null, $merged, [string]$folder, 'merged', $options))
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($xlsx)
try {
    $reader = New-Object IO.StreamReader($zip.GetEntry('xl/worksheets/sheet1.xml').Open())
    try { [xml]$sheet = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $ns = New-Object Xml.XmlNamespaceManager($sheet.NameTable)
    $ns.AddNamespace('s','http://schemas.openxmlformats.org/spreadsheetml/2006/main')
    if ($sheet.SelectSingleNode('//s:c[@r="B2"]/s:v', $ns).InnerText -ne '5') { throw 'Merged Excel quantity incorrect' }
} finally { $zip.Dispose() }
Write-Host 'BOM merge checks passed: grouping, metadata, preview isolation, filtered scope, CSV and Excel.'
