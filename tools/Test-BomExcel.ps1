<# Offline regression: embedded previews, column order, numeric quantity, missing images. #>
[CmdletBinding()]
param([string] $DllPath)
$ErrorActionPreference = 'Stop'
if (-not $DllPath) { $DllPath = Join-Path $PSScriptRoot '..\src\MechKit\bin\Release\MechKit.dll' }
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $DllPath).Path)
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem
$rowType = $assembly.GetType('MechKit.Features.PartListRow', $true)
$optionType = $assembly.GetType('MechKit.Features.PartListOptions', $true)
$exportType = $assembly.GetType('MechKit.Features.BomExcelExporter', $true)
$rows = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($rowType))
foreach ($configuration in @('Default', 'Other', 'Default')) {
    $row = [Activator]::CreateInstance($rowType)
    $row.FilePath = 'sample.sldprt'
    $row.Configuration = $configuration
    $row.Name = '测试 & <零件>'
    $row.Quantity = 3
    $row.PurchaseLink = 'https://example.com/item?id=123&sku=456'
    $rows.Add($row)
}
$options = [Activator]::CreateInstance($optionType)
$options.ColumnOrder = 'quantity,name,sequence'
$bitmap = New-Object Drawing.Bitmap 104,72
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.Clear([Drawing.Color]::SteelBlue)
$memory = New-Object IO.MemoryStream
$bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
$script:previewBytes = $memory.ToArray()
$graphics.Dispose(); $bitmap.Dispose(); $memory.Dispose()
$script:previewCalls = 0
$callbackType = [Func``2].MakeGenericType($rowType, [byte[]])
$callback = [Management.Automation.LanguagePrimitives]::ConvertTo({
    param($row)
    $script:previewCalls++
    if ($row.Configuration -eq 'Other') { return $null }
    return ,$script:previewBytes
}, $callbackType)
[string]$folder = Join-Path $PSScriptRoot '..\artifacts\bom-excel-test'
$method = $exportType.GetMethod('ExportWithPreviews', [Reflection.BindingFlags]'Static,NonPublic')
$path = $method.Invoke($null, @($rows, $folder, 'BOM-thumbnail', $options, $callback))
if ($script:previewCalls -ne 2) { throw 'Preview cache must distinguish configurations and reuse duplicates.' }
$zip = [IO.Compression.ZipFile]::OpenRead($path)
try {
    function Read-Entry($name) {
        $reader = New-Object IO.StreamReader ($zip.GetEntry($name).Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    [xml]$sheet = Read-Entry 'xl/worksheets/sheet1.xml'
    $ns = New-Object Xml.XmlNamespaceManager $sheet.NameTable
    $ns.AddNamespace('s', 'http://schemas.openxmlformats.org/spreadsheetml/2006/main')
    if ($sheet.SelectSingleNode('//s:c[@r="A1"]/s:is/s:t', $ns).InnerText -ne '零件缩略图') { throw 'Missing leading thumbnail column.' }
    if ($sheet.SelectSingleNode('//s:c[@r="B2"]/s:v', $ns).InnerText -ne '3') { throw 'Numeric quantity or column order changed.' }
    if ($sheet.SelectSingleNode('//s:c[@r="C2"]/s:is/s:t', $ns).InnerText -ne '测试 & <零件>') { throw 'Text escaping failed.' }
    if ($sheet.SelectSingleNode('//s:c[@r="A3"]/s:is/s:t', $ns).InnerText -ne '无缩略图') { throw 'Missing-image fallback failed.' }
    [xml]$drawing = Read-Entry 'xl/drawings/drawing1.xml'
    $anchors = $drawing.DocumentElement.ChildNodes
    if ($anchors.Count -ne 2 -or $anchors[0].from.row -ne '1' -or $anchors[1].from.row -ne '3') { throw 'Images do not match BOM rows.' }
    if (-not $zip.GetEntry('xl/media/image1.png') -or -not $zip.GetEntry('xl/media/image2.png')) { throw 'Images are not embedded.' }
    if ($sheet.SelectNodes('//s:hyperlinks/s:hyperlink',$ns).Count -ne 3) { throw 'Purchase hyperlinks missing.' }
    [xml]$relationships=Read-Entry 'xl/worksheets/_rels/sheet1.xml.rels'
    $links=@($relationships.DocumentElement.ChildNodes | Where-Object {$_.TargetMode -eq 'External'})
    if($links.Count -ne 3 -or $links[0].Target -ne 'https://example.com/item?id=123&sku=456'){throw 'Hyperlink target escaping or relationships failed'}
    $elements=@($sheet.DocumentElement.ChildNodes | ForEach-Object LocalName)
    if([Array]::IndexOf($elements,'hyperlinks') -gt [Array]::IndexOf($elements,'drawing')){throw 'Worksheet hyperlink element order invalid'}
} finally { $zip.Dispose() }
Write-Host "BOM Excel regression checks passed: $path"

# A workbook without previews must also be valid and contain no dangling drawing reference.
$emptyCallback = [Management.Automation.LanguagePrimitives]::ConvertTo({ param($row) return $null }, $callbackType)
$emptyPath = $method.Invoke($null, @($rows, $folder, 'BOM-no-preview', $options, $emptyCallback))
$zip = [IO.Compression.ZipFile]::OpenRead($emptyPath)
try {
    if ($zip.GetEntry('xl/drawings/drawing1.xml')) { throw 'Unexpected drawing in no-preview workbook.' }
} finally { $zip.Dispose() }
Write-Host "No-preview workbook checks passed: $emptyPath"

