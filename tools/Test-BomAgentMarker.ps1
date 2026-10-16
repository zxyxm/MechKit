# Offline regression for agent purchase markers and URL-only link actions.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFrom("$repo\src\MechKit\bin\Release\MechKit.dll")
$service = $assembly.GetType('MechKit.Features.PurchaseLinkService', $true)
$partService = $assembly.GetType('MechKit.Features.PartListService', $true)
$rowType = $assembly.GetType('MechKit.Features.PartListRow', $true)
$options = [Activator]::CreateInstance($assembly.GetType('MechKit.Features.PartListOptions', $true))
$resolve = $service.GetMethod('Resolve')
$validate = $service.GetMethod('Validate')
$isWebLink = $service.GetMethod('IsWebLink')
$apply = $partService.GetMethod('ApplyConfiguredFields', [Reflection.BindingFlags]'Static,NonPublic')
$rows = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($rowType))
function Check($ok, $message) { if (-not $ok) { throw $message } }
foreach ($name in @('代理-气动-浮动座', '代理_传感器_扫码枪', 'C:\parts\代理-传动-倍速链组装.SLDASM')) {
    Check ($resolve.Invoke($null, @($name, '')) -eq '代理') 'Agent prefix did not receive marker'
}
foreach ($name in @('淘宝-气动-浮动座', '20261016-6061-代理安装板', '')) {
    Check ($resolve.Invoke($null, @($name, '')) -eq '') 'Non-agent received marker'
}
Check ($resolve.Invoke($null, @('代理-气动-浮动座', 'https://example.com/item')) -eq 'https://example.com/item') 'Existing URL was overwritten'
Check ($resolve.Invoke($null, @('代理-气动-浮动座', '已有备注')) -eq '已有备注') 'Existing nonempty property was overwritten'
Check ($validate.Invoke($null, @(' 代理 ')) -eq '代理') 'Marker rejected by import/save validation'
Check (-not $isWebLink.Invoke($null, @('代理'))) 'Marker treated as web link'
Check ($isWebLink.Invoke($null, @('https://example.com/item'))) 'Web link rejected'
foreach ($invalid in @('javascript:alert(1)', 'file:///C:/test', 'not-a-url')) {
    $failed = $false
    try { $validate.Invoke($null, @($invalid)) | Out-Null } catch { $failed = $true }
    Check $failed 'Invalid URL accepted'
}
foreach ($stored in @('', 'https://example.com/item')) {
    $row = [Activator]::CreateInstance($rowType)
    $row.FullName = '代理-气动-浮动座' + $rows.Count
    $row.Name = '浮动座' + $rows.Count
    $row.Classification = '标准件'; $row.Quantity = 1
    $properties = New-Object 'Collections.Generic.Dictionary[string,string]'
    $properties['购买链接'] = $stored
    $apply.Invoke($null, @($row, $row.FullName, $properties.PSObject.BaseObject, $options)) | Out-Null
    $expected = if ($stored) { $stored } else { '代理' }
    Check ($row.PurchaseLink -eq $expected) 'BOM field mapping lost marker or URL'
    $row.MarkBomClean()
    Check (-not $row.HasBomEdits) 'Automatically derived marker incorrectly dirtied row'
    $rows.Add($row)
}
[string]$folder = Join-Path $repo 'artifacts\bom-agent-test'
[IO.Directory]::CreateDirectory($folder) | Out-Null
$excel = $assembly.GetType('MechKit.Features.BomExcelExporter').GetMethod('Export')
$xlsx = $excel.Invoke($null, @($null, $rows, $folder, 'agent-marker', $options))
$zip = [IO.Compression.ZipFile]::OpenRead($xlsx)
try {
    $reader = New-Object IO.StreamReader ($zip.GetEntry('xl/worksheets/sheet1.xml').Open())
    try { [xml]$sheet = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $ns = New-Object Xml.XmlNamespaceManager $sheet.NameTable
    $ns.AddNamespace('s', 'http://schemas.openxmlformats.org/spreadsheetml/2006/main')
    Check ($sheet.SelectNodes('//s:c/s:is/s:t[text()="代理"]', $ns).Count -eq 1) 'Excel missing plain-text marker'
    Check ($sheet.SelectNodes('//s:hyperlinks/s:hyperlink', $ns).Count -eq 1) 'Excel must link only the URL, never the marker'
} finally { $zip.Dispose() }
$import = $assembly.GetType('MechKit.Features.BomImporter').GetMethod('Import')
foreach ($format in @('csv', 'xlsx')) {
    $path = if ($format -eq 'csv') { $partService.GetMethod('ExportCsv').Invoke($null, @($rows, $folder, 'agent-marker', $options)) } else { $xlsx }
    $rows[0].PurchaseLink = ''
    $result = $import.Invoke($null, @([string]$path, $rows, $options))
    # Both exporters include a trailing summary line without a name, which the importer skips.
    $expectedSkipped = 1
    Check ($result.Updated -eq 2 -and $result.Skipped -eq $expectedSkipped -and $rows[0].PurchaseLink -eq '代理') "Marker round trip failed ($format): updated=$($result.Updated), skipped=$($result.Skipped), value=$($rows[0].PurchaseLink), messages=$($result.Messages -join '; ')"
}
Write-Host 'PASS: agent defaults, existing values, marker validation, URL-only linking, BOM mapping and CSV/Excel round trips.'
