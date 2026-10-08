$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$repoRoot = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $repoRoot 'src\MechKit\bin\Release\MechKit.dll'))
$layout = $assembly.GetType('MechKit.UI.WindowLayout', $true)
$handle = $layout.GetMethod('HandleWindowShortcut', [Reflection.BindingFlags]'NonPublic,Static')
$form = [System.Windows.Forms.Form]::new()
$form.ShowInTaskbar = $false
$form.Opacity = 0
try {
    $layout.GetMethod('EnableEscapeToClose').Invoke($null, @($form))
    $layout.GetMethod('EnableEscapeToClose').Invoke($null, @($form))
    $form.Show()
    $form.Activate()
    [System.Windows.Forms.Application]::DoEvents()
    if (-not $handle.Invoke($null, @([System.Windows.Forms.Keys]::Tab)) -or -not $form.TopMost) {
        throw 'Tab did not bring the window to the top.'
    }
    if ($handle.Invoke($null, @([System.Windows.Forms.Keys]::Enter))) { throw 'Unrelated key was consumed.' }
    if (-not $handle.Invoke($null, @([System.Windows.Forms.Keys]::Escape)) -or -not $form.IsDisposed) {
        throw 'Escape did not close the window.'
    }
    if ($handle.Invoke($null, @([System.Windows.Forms.Keys]::Tab))) { throw 'Closed form remained registered.' }
    Write-Output 'PASS: Tab brings window to top, Escape closes, unrelated keys pass, handlers are removed.'
}
finally { $form.Dispose() }
