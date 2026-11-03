param([string]$PreviewPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$a = [Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot '..\src\MechKit\bin\Release\MechKit.dll'))
$t = $a.GetType('MechKit.Features.RequirementLibrary')
$static = [Reflection.BindingFlags]'Static,NonPublic'
$instance = [Reflection.BindingFlags]'Instance,NonPublic'
$path = Join-Path $env:TEMP ('requirements-test-' + [Guid]::NewGuid().ToString('N') + '.xml')
function Call-Library($obj, $name, [object[]]$arguments) {
    $flags = if ($null -eq $obj) { $static } else { $instance }
    for ($i = 0; $i -lt $arguments.Length; $i++) { if ($null -ne $arguments[$i]) { $arguments[$i] = $arguments[$i].PSObject.BaseObject } }
    return $t.GetMethod($name,$flags).Invoke($obj,$arguments)
}
try {
    $lib = Call-Library $null 'Load' @($path)
    $groups = $t.GetField('Groups',$instance).GetValue($lib)
    $common = @($groups.Keys)[0]
    $material = ([string][char]0x6750) + [char]0x6599 + ':'
    $recipe = ([string][char]0x914d) + [char]0x65b9 + ':'
    $groups[$common] = [Collections.Generic.List[string]]@('Deburr & clean <edges>', 'BOM')
    $groups[$material + 'POM'] = [Collections.Generic.List[string]]@('BOM', 'Plastic requirement')
    $combined = @(Call-Library $lib 'Combined' @('pom'))
    if ($combined.Count -ne 3 -or $combined[2] -ne 'Plastic requirement') { throw 'Material combination or deduplication failed' }
    $groups[$recipe + 'Trial'] = [Collections.Generic.List[string]]$combined
    Call-Library $lib 'Save' @($path)
    $loaded = Call-Library $null 'Load' @($path)
    $saved = @(Call-Library $loaded 'Get' @($recipe + 'Trial'))
    if (($saved -join '|') -ne ($combined -join '|')) { throw 'Unicode group / XML escaping roundtrip failed' }
    $groups[$common].Add('New common item')
    if (@(Call-Library $lib 'Get' @($recipe + 'Trial')).Count -ne 3) { throw 'Recipe changed with common group' }
    Call-Library $lib 'Save' @($path)
    $text = Call-Library $null 'Compose' @(,[string[]]@(' ', ' A ', 'A', 'B'))
    if (-not $text.EndsWith("1. A`r`n2. B")) { throw 'Numbering failed' }
    if ((Call-Library $null 'Compose' @(,[string[]]@())) -ne '') { throw 'Empty selection generated a note' }
    $service = $a.GetType('MechKit.Features.TechnicalRequirements')
    try { $service.GetMethod('Insert',$static).Invoke($null,@($null,'text',$false,20.0,70.0)); throw 'Insert without drawing succeeded' }
    catch { $errorCause = $_.Exception; while ($errorCause.InnerException) { $errorCause = $errorCause.InnerException }; if ($errorCause -isnot [InvalidOperationException]) { throw } }

    $h = [Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot 'MechKitHarness\bin\Release\MechKitHarness.exe'))
    $fake = [Activator]::CreateInstance($h.GetType('MechKit.Harness.FakeHost'),$true)
    $formType = $a.GetType('MechKit.UI.TechnicalRequirementsForm')
    $form = [Activator]::CreateInstance($formType,$instance,$null,@($fake),$null)
    try {
        $form.Show()
        [Windows.Forms.Application]::DoEvents()
        $items = $formType.GetField('_items',$instance).GetValue($form)
        $preview = $formType.GetField('_preview',$instance).GetValue($form)
        if (-not $form.TopMost -or $items.Items.Count -eq 0) { throw 'Initial UI state failed' }
        $items.SetItemChecked(0,$true)
        [Windows.Forms.Application]::DoEvents()
        if (-not $preview.Text.Contains('1. ')) { throw 'Check selection did not update preview' }
        $items.SelectedIndex = 0
        $first = $items.Items[0]
        $formType.GetMethod('MoveItem',$instance).Invoke($form,@(1))
        if ($items.Items[1] -ne $first -or -not $items.GetItemChecked(1)) { throw 'Reorder lost selection' }
        if ($PreviewPath) {
            $bmp = New-Object Drawing.Bitmap $form.Width,$form.Height
            try { $form.DrawToBitmap($bmp,(New-Object Drawing.Rectangle 0,0,$form.Width,$form.Height)); $bmp.Save($PreviewPath) }
            finally { $bmp.Dispose() }
        }
    } finally { $form.Dispose() }
    Write-Host 'Technical requirements checks passed: persistence, combinations, recipes, numbering, guards, UI selection and reorder.'
} finally { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path } }
