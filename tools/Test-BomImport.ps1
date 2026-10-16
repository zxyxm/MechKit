# Offline import regression: no SOLIDWORKS connection or model writes.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Windows.Forms
$repo = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFrom("$repo\src\MechKit\bin\Release\MechKit.dll")
$rowType = $assembly.GetType('MechKit.Features.PartListRow', $true)
$optionsType = $assembly.GetType('MechKit.Features.PartListOptions', $true)
$importMethod = $assembly.GetType('MechKit.Features.BomImporter', $true).GetMethod('Import')
$options = [Activator]::CreateInstance($optionsType)
[string]$folder = Join-Path $repo 'artifacts\bom-import-test'
[IO.Directory]::CreateDirectory($folder) | Out-Null
$utf8 = New-Object Text.UTF8Encoding $true
function Check($condition, $message) { if (-not $condition) { throw $message } }
function Rows { return ,([Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($rowType))) }
function Row($full, $name, $location = '总装') {
    $row = [Activator]::CreateInstance($rowType)
    $row.FullName = $full; $row.Name = $name; $row.Location = $location
    $row.FilePath = "C:\models\$full.sldprt"; $row.Configuration = 'Default'
    $row.Material = '旧材料'; $row.Process = '旧工艺'; $row.SurfaceTreatment = '旧表面'
    $row.Quantity = 1; $row.AssemblyNote = '旧安装'; $row.Remark = '旧备注'
    $row.PurchaseLink = 'https://example.com/old'; $row.MarkBomClean()
    return $row
}
function ImportText($text, $rows) {
    [string]$path = Join-Path $folder 'input.csv'
    [IO.File]::WriteAllText($path, $text, $utf8)
    return $importMethod.Invoke($null, @($path, $rows, $options))
}
$rows = Rows
$rows.Add((Row 'Plate-A' '支撑板')); $rows.Add((Row 'Pin-B' '销钉'))
$result = ImportText @'
备注,数量,完整名称,工艺/渠道,材料/型号,购买链接,安装说明,表面处理
"含逗号,双引号""和
换行",4, plate-a.SLDPRT ,铣,6061,https://example.com/new,朝上,阳极氧化
'@ $rows
Check ($result.Updated -eq 1 -and $result.Skipped -eq 0) 'Seven-column import did not match'
$row = $rows[0]
Check ($row.Material -eq '6061' -and $row.Process -eq '铣' -and $row.SurfaceTreatment -eq '阳极氧化' -and $row.Quantity -eq 4) 'Core values wrong'
Check ($row.AssemblyNote -eq '朝上' -and $row.PurchaseLink -eq 'https://example.com/new' -and $row.Remark.Contains('双引号"和') -and $row.Remark.Contains("`n")) 'Quoted CSV or text values lost'
Check ($row.HasBomEdits -and $row.OriginalSurfaceTreatment -eq '旧表面') 'Import was marked clean prematurely'
Check ($rows[1].Material -eq '旧材料' -and -not $rows[1].HasBomEdits) 'Unmatched target changed'

$result = ImportText "零件名称/标准件名称,材料/型号,备注`r`n销钉,304,`r`n" $rows
Check ($result.Updated -eq 1 -and $rows[1].Material -eq '304' -and $rows[1].Remark -eq '旧备注' -and $rows[1].Quantity -eq 1) 'Name fallback or blank/missing preservation failed'
$options.MaterialHeader = '指定材料'
$result = ImportText "名称,指定材料`n销钉,316" $rows
Check ($rows[1].Material -eq '316') 'Custom column header not mapped'
$result = ImportText "名称,数量,材料/型号`n销钉,-1,错误`n支撑板,1.5,错误" $rows
Check ($result.Updated -eq 0 -and $result.Skipped -eq 2 -and $rows[1].Material -eq '316') 'Invalid quantity partially mutated a row'
$result = ImportText "名称,购买链接,备注`n销钉,javascript:alert(1),错误" $rows
Check ($result.Updated -eq 0 -and $rows[1].Remark -eq '旧备注') 'Invalid link partially applied'
$result = ImportText "名称,数量`n销钉,2.0" $rows
Check ($rows[1].Quantity -eq 2) 'Integral numeric Excel value rejected'
$result = ImportText "完整名称,名称,数量`nUnknown,销钉,99" $rows
Check ($result.Updated -eq 0 -and $rows[1].Quantity -eq 2) 'Authoritative full name fell back to wrong short name'
$result = ImportText "名称,数量`n销钉,3`n销钉,4" $rows
Check ($result.Updated -eq 0 -and $result.Skipped -eq 2 -and $rows[1].Quantity -eq 2) 'Duplicate import records silently overwrote'
$rows.Add((Row 'Pin-C' '销钉' '子装配'))
$result = ImportText "名称,数量`n销钉,3" $rows
Check ($result.Updated -eq 0 -and $result.Skipped -eq 1) 'Ambiguous short name updated multiple targets'
$result = ImportText "名称,位置,配置,数量`n销钉,子装配,Default,3" $rows
Check ($result.Updated -eq 1 -and $rows[2].Quantity -eq 3 -and $rows[1].Quantity -eq 2) 'Location/configuration disambiguation failed'
foreach ($invalid in @("数量,备注`n1,test", "名称,数量,数量`n支撑板,1,2", "名称,数量`n支撑板,8`n销钉,`"unfinished")) {
    $failed = $false
    try { ImportText $invalid $rows | Out-Null } catch { $failed = $true }
    Check $failed 'Invalid file did not fail'
    Check ($rows[0].Quantity -eq 4) 'Invalid file partially mutated preview'
}
$result = ImportText "sep=;`r`n名称;材料/型号`r`n支撑板;7075" $rows
Check ($rows[0].Material -eq '7075') 'Excel separator directive unsupported'
[string]$gbPath = Join-Path $folder 'gbk.csv'
[IO.File]::WriteAllText($gbPath, "名称,工艺/渠道`r`n支撑板,车铣", [Text.Encoding]::GetEncoding(936))
$importMethod.Invoke($null, @($gbPath, $rows, $options)) | Out-Null
Check ($rows[0].Process -eq '车铣') 'GBK CSV corrupted Chinese'

# Round trip the actual MechKit Excel exporter (reordered columns, images absent, inline strings and hyperlinks).
$options.ColumnOrder = 'remark,quantity,name,material'
$exporter = $assembly.GetType('MechKit.Features.BomExcelExporter').GetMethod('Export')
$xlsx = $exporter.Invoke($null, @($null, $rows, $folder, 'roundtrip', $options))
$targets = Rows
foreach ($source in $rows) { $targets.Add((Row $source.FullName $source.Name $source.Location)) }
$result = $importMethod.Invoke($null, @($xlsx, $targets, $options))
Check ($result.Updated -eq 3) 'Exported XLSX failed round trip'
Check ($targets[0].Quantity -eq 4 -and $targets[0].PurchaseLink -eq $rows[0].PurchaseLink -and $targets[0].Remark -eq $rows[0].Remark) 'XLSX round trip lost values'
$csvExporter = $assembly.GetType('MechKit.Features.PartListService').GetMethod('ExportCsv')
$csv = $csvExporter.Invoke($null, @($rows, $folder, 'roundtrip', $options))
$result = $importMethod.Invoke($null, @($csv, $targets, $options))
Check ($result.Updated -eq 3) 'Exported CSV failed round trip'

# Shared strings, sparse cells, a leading title, arbitrary sheet path and hyperlink display labels.
[string]$fixture = Join-Path $folder 'shared-strings.xlsx'
$file = [IO.File]::Create($fixture)
$zip = New-Object IO.Compression.ZipArchive $file, ([IO.Compression.ZipArchiveMode]::Create)
function Entry($name, $content) {
    $writer = New-Object IO.StreamWriter ($zip.CreateEntry($name).Open())
    try { $writer.Write($content) } finally { $writer.Dispose() }
}
try {
    Entry 'xl/workbook.xml' '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="BOM" sheetId="8" r:id="rSheet"/></sheets></workbook>'
    Entry 'xl/_rels/workbook.xml.rels' '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rSheet" Target="worksheets/custom.xml" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"/><Relationship Id="rStrings" Target="sharedStrings.xml" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings"/></Relationships>'
    Entry 'xl/sharedStrings.xml' '<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><si><t>完整名称</t></si><si><t>数量</t></si><si><t>购买链接</t></si><si><r><t>Plate</t></r><r><t>-A</t></r></si><si><t>打开商品</t></si></sst>'
    Entry 'xl/worksheets/custom.xml' '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>采购清单</t></is></c></row><row r="3"><c r="B3" t="s"><v>0</v></c><c r="D3" t="s"><v>1</v></c><c r="F3" t="s"><v>2</v></c></row><row r="4"><c r="B4" t="s"><v>3</v></c><c r="D4"><f>2*3</f><v>6</v></c><c r="F4" t="s"><v>4</v></c></row></sheetData><hyperlinks><hyperlink ref="B4" r:id="nameLink"/><hyperlink ref="F4" r:id="buyLink"/></hyperlinks></worksheet>'
    Entry 'xl/worksheets/_rels/custom.xml.rels' '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="nameLink" Target="https://example.com/model" TargetMode="External"/><Relationship Id="buyLink" Target="https://example.com/product?id=1&amp;sku=2" TargetMode="External"/></Relationships>'
} finally { $zip.Dispose(); $file.Dispose() }
$result = $importMethod.Invoke($null, @($fixture, $targets, $options))
Check ($result.Updated -eq 1 -and $targets[0].Quantity -eq 6 -and $targets[0].PurchaseLink -eq 'https://example.com/product?id=1&sku=2') 'Sparse XLSX/shared strings/hyperlink labels/cached formula failed'

# Each newly supported persisted attribute participates in the existing apply workflow.
foreach ($property in @('SurfaceTreatment', 'AssemblyNote', 'PurchaseLink')) {
    $probe = Row 'Probe' '测试'
    $probe.$property = 'new'
    Check $probe.HasBomEdits "$property not marked dirty"
    $probe.MarkBomClean()
    Check (-not $probe.HasBomEdits) "$property did not become clean"
}
$probe = Row 'Probe' '测试'; $probe.Quantity = 99
Check (-not $probe.HasBomEdits) 'Quantity must not be saved to the model'

# Exercise the real form integration, including filter scope and preserving export selections.
$harness = [Reflection.Assembly]::LoadFrom("$repo\tools\MechKitHarness\bin\Release\MechKitHarness.exe")
$hostObject = [Activator]::CreateInstance($harness.GetType('MechKit.Harness.FakeHost'), $true)
$formType = $assembly.GetType('MechKit.UI.PartListForm')
$form = [Activator]::CreateInstance($formType, @($hostObject))
$flags = [Reflection.BindingFlags]'NonPublic,Instance'
try {
    $form.ShowInTaskbar = $false
    $form.Opacity = 0
    $form.Show()
    [Windows.Forms.Application]::DoEvents()
    $formType.GetMethod('RunSummary', $flags).Invoke($form, @()) | Out-Null
    $formRows = $formType.GetField('_rows', $flags).GetValue($form)
    $grid = $formType.GetField('_grid', $flags).GetValue($form)
    $search = $formType.GetField('_keywordSearch', $flags).GetValue($form)
    $button = $formType.GetField('_importButton', $flags).GetValue($form)
    Check ($button.Text -eq '导入 BOM…' -and $button.Enabled) 'Import action not available'
    $formRows[0].FullName = 'ImportTarget'; $formRows[1].FullName = 'OutsideScope'
    $search.Text = 'ImportTarget'
    $formType.GetMethod('ShowFilteredRows', $flags).Invoke($form, @($false)) | Out-Null
    $grid.Rows[0].Cells['select'].Value = $true
    [string]$formCsv = Join-Path $folder 'form.csv'
    [IO.File]::WriteAllText($formCsv, "完整名称,材料/型号,表面处理,数量,安装说明,备注,购买链接`nImportTarget,TEST,黑色,7,向上,导入备注,https://example.com/import`nOutsideScope,WRONG,错误,99,错误,错误,https://example.com/wrong", $utf8)
    $formType.GetMethod('ImportBomFile', $flags).Invoke($form, @($formCsv)) | Out-Null
    Check ($formRows[0].Material -eq 'TEST' -and $formRows[0].Quantity -eq 7 -and $formRows[0].AssemblyNote -eq '向上') 'Form did not import values'
    Check ($formRows[1].Material -ne 'WRONG') 'Import ignored current filter scope'
    Check ($grid.Rows[0].Cells['surface'].Value -eq '黑色' -and $grid.Rows[0].Cells['select'].Value) 'Grid refresh lost imported data or export marks'
    $formType.GetMethod('PullGridEdits', $flags).Invoke($form, @()) | Out-Null
    Check ($formRows[0].PurchaseLink -eq 'https://example.com/import' -and $formRows[0].HasBomEdits) 'Export preparation lost staged attributes'
    $search.Clear()
    $formType.GetMethod('ShowFilteredRows', $flags).Invoke($form, @($false)) | Out-Null
    $form.ClientSize = New-Object Drawing.Size 1080, 640
    $form.PerformLayout()
    Check ($button.Right -le $button.Parent.ClientSize.Width) 'Import button is clipped at minimum width'
    foreach ($control in $button.Parent.Controls) {
        Check ($control.Right -le $button.Parent.ClientSize.Width) 'Import button crowded another toolbar control'
    }
    $bitmap = New-Object Drawing.Bitmap $form.Width, $form.Height
    try {
        $form.DrawToBitmap($bitmap, (New-Object Drawing.Rectangle 0, 0, $form.Width, $form.Height))
        $bitmap.Save((Join-Path $folder 'import-preview.png'), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
} finally { $form.Dispose() }
Write-Host 'PASS: BOM CSV/XLSX import, seven fields, aliases, encoding, round trips, validation, ambiguity, dirty tracking and filtered UI integration.'
