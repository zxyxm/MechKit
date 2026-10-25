$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.Windows.Forms
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $repo 'src/MechKit/bin/Release/MechKit.dll'))
$harness = [Reflection.Assembly]::LoadFrom((Join-Path $repo 'tools/MechKitHarness/bin/Release/MechKitHarness.exe'))
$fakeHost = [Activator]::CreateInstance($harness.GetType('MechKit.Harness.FakeHost'), $true)
$type = $assembly.GetType('MechKit.UI.NamingRuleForm')
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
$form = [Activator]::CreateInstance($type, @($fakeHost))
try {
    $inputBox = $type.GetField('_newMachinedLevel2', $flags).GetValue($form)
    $add = $type.GetMethod('AddTypedMachinedLevel2', $flags)
    $rows = $type.GetField('_machinedLevel2Rows', $flags).GetValue($form)
    $before = $rows.Count
    for ($i = 0; $i -lt 20; $i++) {
        $inputBox.Text = 'ExpansionTest' + $i
        $add.Invoke($form, @())
    }
    if ($rows.Count -ne $before + 20) { throw 'Material editor still limits additions.' }
    $stored = $type.GetField('_machinedLevel2Values', $flags).GetValue($form).Text
    if (-not $stored.Contains('ExpansionTest19')) { throw 'Last material was not serialized.' }
    $addin = [Activator]::CreateInstance($assembly.GetType('MechKit.MechKitAddin'))
    $settings = $fakeHost.Settings
    $settings.MachinedLevel2Values = $stored
    $settings.MachinedTabLevel2Values = $stored
    $addin.GetType().GetField('_settings', $flags).SetValue($addin, $settings)
    $buttons = $addin.GetType().GetMethod('BuildMachinedLevel2ButtonList', $flags).Invoke($addin, @())
    if (-not $buttons.Contains('ExpansionTest19')) { throw 'Shortcut list truncated materials.' }
    $type.GetMethod('RebuildMachinedLevel2Rows', $flags).Invoke($form, @())
    if ($rows.Count -ne $before + 20) { throw 'Editor reload lost rows.' }
    Write-Host 'PASS: 20 extra material rows, serialization, editor reload and shortcut inclusion.'
} finally { $form.Dispose() }
