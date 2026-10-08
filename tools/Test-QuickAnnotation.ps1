$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $repoRoot 'src\MechKit\bin\Release\MechKit.dll'))
$service = $assembly.GetType('MechKit.Features.QuickAnnotationService', $true)
$presets = $service.GetField('Presets').GetValue($null)
$expected = @(@(0.0,0.00005,2,$false),@(-0.00005,0.0,2,$false),@(0.0,0.000012,3,$true))
if ($presets.Length -ne 3) { throw 'Missing presets.' }
for ($i = 0; $i -lt 3; $i++) {
    $preset = $presets[$i]
    if ([Math]::Abs($preset.LowerMeters - $expected[$i][0]) -gt 1e-12 -or
        [Math]::Abs($preset.UpperMeters - $expected[$i][1]) -gt 1e-12 -or
        $preset.Precision -ne $expected[$i][2] -or $preset.DiameterOnly -ne $expected[$i][3]) {
        throw ('Wrong tolerance, conversion, or precision in preset ' + $i)
    }
}
$message = $service.GetMethod('Apply').Invoke($null, @($null, $presets[0]))
if ([string]::IsNullOrWhiteSpace($message)) { throw 'Missing no-document feedback.' }
$constants = $assembly.GetType('MechKit.AddinConstants', $true)
$command = $constants.GetField('CmdQuickAnnotation').GetValue($null)
if ($constants.GetField('CommandIds').GetValue($null) -notcontains $command) { throw 'Command missing from catalog.' }
if (-not $assembly.GetType('MechKit.MechKitAddin').GetMethod('OnQuickAnnotation')) { throw 'Missing COM callback.' }
Write-Output 'PASS: preset signs, millimeter conversion, precision, diameter restriction and command registration.'
