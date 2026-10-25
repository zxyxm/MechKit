$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$repo = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFrom("$repo\src\MechKit\bin\Release\MechKit.dll")
$type = $assembly.GetType('MechKit.Core.OperationTrace', $true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
function TraceCall($name, [object[]]$values) {
    $type.GetMethod($name, $flags).Invoke($null, $values)
}
TraceCall 'Start' @()
$outer = TraceCall 'Begin' @('test outer')
$inner = TraceCall 'Begin' @('test nested')
$inner.Dispose()
TraceCall 'Event' @("test multiline`r`nflattened")
try {
    $errorScope = TraceCall 'Begin' @('test exception unwind')
    try { throw 'expected test error' } finally { $errorScope.Dispose() }
} catch { }
# Deliberately stop pumping the UI thread. The watchdog must still write.
Start-Sleep -Seconds 12
[Windows.Forms.Application]::DoEvents()
Start-Sleep -Seconds 2
$outer.Dispose()
$outer.Dispose()
TraceCall 'Stop' @()
Start-Sleep -Seconds 2
$file = Get-ChildItem "$env:LOCALAPPDATA\MechKit\logs\Operations-*-$PID.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$text = [IO.File]::ReadAllText($file.FullName)
foreach ($pattern in @('SESSION START','BEGIN #1 test outer','END #1 test outer elapsedMs=', 'BEGIN #2 test nested','END #2 test nested', 'END #3 test exception unwind','UI_STALL.*active=1:test outer','UI_RECOVERED','SESSION STOP','test multiline  flattened')) {
    if ($text -notmatch $pattern) { throw "Missing diagnostic: $pattern" }
}
if ([regex]::Matches($text, 'END #1 ').Count -ne 1) { throw 'Scope disposal was not idempotent' }
Write-Output "PASS: nested operations, exception unwind, elapsed time, stalled UI watchdog, recovery, clean shutdown. Log: $($file.FullName)"
