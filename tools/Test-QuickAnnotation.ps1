[CmdletBinding()]
param([string] $PreviewPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$repo = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $repo 'tools\MechKitHarness\bin\Release\MechKit.dll'))
$harness = [Reflection.Assembly]::LoadFrom((Join-Path $repo 'tools\MechKitHarness\bin\Release\MechKitHarness.exe'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $repo 'tools\MechKitHarness\bin\Release\SolidWorks.Interop.swconst.dll'))
$presetType = $assembly.GetType('MechKit.Features.TolerancePreset', $true)
$service = $assembly.GetType('MechKit.Features.QuickAnnotationService', $true)
$parse = $presetType.GetMethod('Parse')
$checked = 0
function Check($label, $condition) {
    if (-not $condition) { throw "FAIL: $label" }
    $script:checked++
    Write-Host "PASS: $label"
}
function Preset([string] $text) { return $parse.Invoke($null, @($text)) }
$p = Preset '0.03/0/销钉'
Check 'upper tolerance converted from mm to metres' ([Math]::Abs($p.UpperMeters - 0.00003) -lt 1e-12)
Check 'zero lower and two decimal places retained' ($p.LowerMeters -eq 0 -and $p.Precision -eq 2)
Check 'remark is a preset label' ($p.Note -eq '销钉' -and $p.Label.Contains('销钉'))
$p = Preset '0/-0.05/负公差'
Check 'negative lower is preserved' ([Math]::Abs($p.LowerMeters + 0.00005) -lt 1e-12 -and $p.UpperMeters -eq 0)
$p = Preset '-0.02/-0.05/孔用'
Check 'both deviations can be negative' ($p.UpperMeters -lt 0 -and $p.LowerMeters -lt $p.UpperMeters)
$p = Preset '±0.020'
Check 'symmetric type and sign' ($p.Type -eq [int][SolidWorks.Interop.swconst.swTolType_e]::swTolSYMMETRIC -and $p.LowerMeters -eq -$p.UpperMeters)
Check 'trailing zeros preserve tolerance precision' ($p.Precision -eq 3)
$p = Preset '+/-0.05'
Check 'ASCII symmetric notation supported' ([Math]::Abs($p.UpperMeters - 0.00005) -lt 1e-12)
$p = Preset '0. 012/0'
Check 'spaced input retains micron precision' ($p.Precision -eq 3 -and [Math]::Abs($p.UpperMeters - 0.000012) -lt 1e-12)
$p = Preset 'H7/孔配合'
Check 'uppercase fit uses hole field' ($p.HoleFit -eq 'H7' -and $p.ShaftFit -eq '' -and $p.Type -eq [int][SolidWorks.Interop.swconst.swTolType_e]::swTolFIT)
$p = Preset 'h7'
Check 'lowercase fit uses shaft field' ($p.HoleFit -eq '' -and $p.ShaftFit -eq 'h7')
$p = Preset '清除公差'
Check 'clear command removes tolerance type' ($p.Type -eq [int][SolidWorks.Interop.swconst.swTolType_e]::swTolNONE)
foreach ($invalid in @('', '0', '-0.05/0', 'NaN/0', 'Infinity/0', '1e3/0', '±-0.05', '0.000000001/0', 'H99', '0,03/0')) {
    $rejected = $false
    try { [void](Preset $invalid) } catch { $rejected = $true }
    Check ("invalid input rejected: '$invalid'") $rejected
}
$defaults = $service.GetMethod('DefaultCells')
$cells = $defaults.Invoke($null, @(0))
foreach ($value in $cells) { if ($value) { [void](Preset $value) } }
Check 'all default tolerances parse' ($cells.Length -eq 35)
$cells[19] = '0.02/-0.01/中文=备注/分隔符'
$cells[20] = ''
$argsEncode = New-Object object[] 1
$argsEncode[0] = [string[]]$cells
$encoded = $service.GetMethod('EncodeCells').Invoke($null, $argsEncode)
$roundtrip = $service.GetMethod('DecodeCells').Invoke($null, @([string]$encoded, 0))
Check 'preset persistence retains Chinese and separators' ($roundtrip[19] -eq $cells[19])
Check 'empty saved preset stays empty' ($roundtrip[20] -eq '')
Check 'preset serialization fits one ini line' (-not $encoded.Contains("`n") -and -not $encoded.Contains("`r"))
$bad = $service.GetMethod('DecodeCells').Invoke($null, @('invalid%.', 0))
Check 'corrupt configuration opens with empty cell' ($bad[0] -eq '' -and $bad.Length -eq 35)
$apply = $service.GetMethod('Apply')
Check 'no active SW does not change any document' ($apply.Invoke($null, @($null, '±0.02', 0, $null)) -eq '请先打开工程图并选择尺寸。')
Check 'no SW returns no hole parameters' ($service.GetMethod('GetHoleParameters').Invoke($null, @($null)).Count -eq 0)
$parameterType = $assembly.GetType('MechKit.Features.HoleParameter', $true)
$parameters = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($parameterType))
$resolve = $service.GetMethod('ResolveHoleParameter', [Reflection.BindingFlags]'Static, NonPublic')
$resolveArgs = New-Object object[] 2
$resolveArgs[0] = $parameters
$resolveArgs[1] = $null
Check 'ordinary dimension needs no hole parameter' ($null -eq $resolve.Invoke($null, $resolveArgs))
$diameter = [Activator]::CreateInstance($parameterType)
$diameter.Name = 'HOLE-DIAMETER'; $diameter.Label = '孔径'
$parameters.Add($diameter)
Check 'single hole parameter applies without manual reading' ($resolve.Invoke($null, $resolveArgs) -eq 'HOLE-DIAMETER')
$resolveArgs[1] = 'OLD-PARAMETER'
Check 'stale parameter is replaced for a single hole' ($resolve.Invoke($null, $resolveArgs) -eq 'HOLE-DIAMETER')
$depth = [Activator]::CreateInstance($parameterType)
$depth.Name = 'HOLE-DEPTH'; $depth.Label = '深度'
$parameters.Add($depth)
Check 'multiple hole parameters require a choice' ($null -eq $resolve.Invoke($null, $resolveArgs))
$resolveArgs[1] = 'HOLE-DEPTH'
Check 'explicit depth choice is honored' ($resolve.Invoke($null, $resolveArgs) -eq 'HOLE-DEPTH')
$fakeHost = [Activator]::CreateInstance($harness.GetType('MechKit.Harness.FakeHost', $true), $true)
$fakeHost.Settings.ToleranceCells = $encoded
$formType = $assembly.GetType('MechKit.UI.QuickAnnotationForm', $true)
$form = [Activator]::CreateInstance($formType, @($fakeHost))
$flags = [Reflection.BindingFlags]'Instance, NonPublic'
try {
    $form.Show()
    [Windows.Forms.Application]::DoEvents()
    $tabs = $formType.GetField('_tabs', $flags).GetValue($form)
    $grids = $formType.GetField('_grids', $flags).GetValue($form)
    $tools = $formType.GetField('_annotationTools', $flags).GetValue($form)
    Check 'all four drawing tools are in the assistant window' (($tools | ForEach-Object Text) -join ',' -eq '智能尺寸,公差助手,孔标注,销钉符号')
    $tabs.SelectedIndex = 2
    $tools[1].PerformClick()
    Check 'tolerance button returns to tolerance page' ($tabs.SelectedIndex -eq 0)
    $status = $formType.GetField('_status', $flags).GetValue($form)
    foreach ($index in @(0, 2, 3)) {
        $tools[$index].PerformClick()
        Check ('native tool requires an open drawing: ' + $tools[$index].Text) ($status.Text -eq ('请先打开工程图，再使用' + $tools[$index].Text + '。'))
    }
    Check 'assistant contains tolerance prefix suffix pages' ($tabs.TabPages.Count -eq 3)
    Check 'tolerance grid has 35 editable preset positions' ($grids[0].RowCount -eq 7 -and $grids[0].ColumnCount -eq 5)
    Check 'saved preset is displayed with its label' ($grids[0].Rows[3].Cells[4].Value.Contains('中文=备注/分隔符'))
    Check 'empty cell displays blank' ([string]::IsNullOrEmpty($grids[0].Rows[4].Cells[0].Value))
    Check 'presets cannot be changed accidentally by typing' ($grids[0].ReadOnly)
    Check 'last row fits in the grid' ($grids[0].GetRowDisplayRectangle(6, $false).Bottom -le $grids[0].Height)
    if ($PreviewPath) {
        $bitmap = New-Object Drawing.Bitmap($form.Width, $form.Height)
        try { $form.DrawToBitmap($bitmap, (New-Object Drawing.Rectangle(0, 0, $form.Width, $form.Height))); $bitmap.Save([IO.Path]::GetFullPath($PreviewPath), [Drawing.Imaging.ImageFormat]::Png) }
        finally { $bitmap.Dispose() }
    }
    $form.ClientSize = New-Object Drawing.Size(490, 560)
    [Windows.Forms.Application]::DoEvents()
    Check 'grid remains fully visible at minimum size' ($grids[0].GetRowDisplayRectangle(6, $false).Bottom -le $grids[0].Height)
}
finally { $form.Dispose() }
Write-Host "All $checked tolerance checks passed."
