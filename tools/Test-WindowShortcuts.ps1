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
    $editor = [System.Windows.Forms.Form]::new()
    $editor.ShowInTaskbar=$false
    $editor.Opacity=0
    $inputBox=[System.Windows.Forms.TextBox]::new()
    $inputBox.Text='unsaved purchase link'
    $editor.Controls.Add($inputBox)
    $timer=[System.Windows.Forms.Timer]::new()
    $timer.Interval=100
    $script:modalError=$null
    $timer.add_Tick({
        $timer.Stop()
        try {
            $inputBox.Focus() | Out-Null
            if($handle.Invoke($null,@([System.Windows.Forms.Keys]::Tab))) {throw 'Tab stolen from modal editor'}
            if(-not $handle.Invoke($null,@([System.Windows.Forms.Keys]::Escape))) {throw 'Modal Escape was not handled'}
            if($form.IsDisposed -or -not $form.Visible) {throw 'Escape closed the BOM owner'}
        } catch { $script:modalError=$_.Exception.Message; $editor.Close() }
    })
    try {
        $timer.Start()
        $result=$editor.ShowDialog($form)
        if($script:modalError){throw $script:modalError}
        if($result -ne [Windows.Forms.DialogResult]::Cancel){throw 'Modal Escape did not cancel edit'}
    } finally { $timer.Dispose();$editor.Dispose() }
    Write-Output 'PASS: Escape cancels modal editor first, BOM remains visible, Tab stays in editor.'
    $form.Activate()
    [System.Windows.Forms.Application]::DoEvents()
    if (-not $handle.Invoke($null, @([System.Windows.Forms.Keys]::Escape)) -or -not $form.IsDisposed) {
        throw 'Escape did not close the window.'
    }
    if ($handle.Invoke($null, @([System.Windows.Forms.Keys]::Tab))) { throw 'Closed form remained registered.' }
    Write-Output 'PASS: Tab brings window to top, Escape closes, unrelated keys pass, handlers are removed.'
}
finally { $form.Dispose() }
