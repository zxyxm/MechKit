$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$repo = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFrom("$repo\src\MechKit\bin\Release\MechKit.dll")
$type = $assembly.GetType('MechKit.Core.DesignNoteStore',$true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
function CallNote($name,[object[]]$arguments) {
    $method=$type.GetMethod($name,$flags)
    $parameters=$method.GetParameters()
    for($i=0;$i -lt $arguments.Length;$i++) {
        $arguments[$i]=[Management.Automation.LanguagePrimitives]::ConvertTo($arguments[$i],$parameters[$i].ParameterType)
    }
    $method.Invoke($null,$arguments)
}
$fixture = Get-Content (Join-Path $PSScriptRoot 'testdata\design-note.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$root = Join-Path $repo ('artifacts\design-note-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
[string]$model = Join-Path $root ($fixture.model + '.sldasm')
[IO.File]::WriteAllText($model,'fixture')
$note=CallNote 'Save' @($model,[string]$fixture.note,$false)
if($note.Text -ne $fixture.note -or -not $note.CreatedAt -or $note.Completed) { throw 'New note state incorrect' }
$created=$note.CreatedAt
$note=CallNote 'Save' @($model,[string]$fixture.note,$true)
if(-not $note.Completed -or -not $note.CompletedAt -or $note.CreatedAt -ne $created) { throw 'Completion or creation time incorrect' }
$completed=$note.CompletedAt
$note=CallNote 'Save' @($model,[string]$fixture.note,$true)
if($note.CompletedAt -ne $completed) { throw 'Repeated completion changed timestamp' }
$note=CallNote 'Save' @($model,[string]$fixture.note,$false)
if($note.Completed -or $note.CreatedAt -ne $created) { throw 'Reopen changed creation time or retained completion' }
$note=CallNote 'Save' @($model,[string]$fixture.note,$true)
$note=CallNote 'Save' @($model,([string]$fixture.note + ' revised'),$true)
if($note.Completed -or $note.CreatedAt -ne $created) { throw 'Edited completed note not reopened' }
$note=CallNote 'Save' @($model,[string]$fixture.note,$false)
$rowType=$assembly.GetType('MechKit.Features.PartListRow',$true)
$row=[Activator]::CreateInstance($rowType)
$row.FilePath=$model
if($row.DesignNote -ne $fixture.note) { throw 'BOM note not linked' }
$instanceFlags=[Reflection.BindingFlags]'Instance,NonPublic'
$panelType=$assembly.GetType('MechKit.UI.DesignNotePanel',$true)
$panel=[Activator]::CreateInstance($panelType,$instanceFlags,$null,@(),$null)
$form=New-Object Windows.Forms.Form
$form.ClientSize=New-Object Drawing.Size 350,220
$form.ShowInTaskbar=$false
$form.Opacity=0
$form.Controls.Add($panel)
try {
    $panelType.GetMethod('Bind',$instanceFlags).Invoke($panel,@($model,[string]$fixture.model))
    $form.Show()
    [Windows.Forms.Application]::DoEvents()
    $text=$panelType.GetField('_text',$instanceFlags).GetValue($panel)
    $check=$panelType.GetField('_completed',$instanceFlags).GetValue($panel)
    if($text.Text -ne $fixture.note -or $check.Checked) { throw 'Panel did not bind current model' }
    $check.Checked=$true
    $note=CallNote 'Load' @($model)
    if(-not $note.Completed) { throw 'Panel checkbox did not persist completion' }
    $bitmap=New-Object Drawing.Bitmap $form.ClientSize.Width,$form.ClientSize.Height
    try { $form.DrawToBitmap($bitmap,$form.ClientRectangle); $bitmap.Save((Join-Path $root 'design-note.png')) }
    finally { $bitmap.Dispose() }
    $panelType.GetMethod('Bind',$instanceFlags).Invoke($panel,@($null,$null))
    if($panel.Enabled -or $text.Text) { throw 'No-document state retained stale note' }
} finally { $form.Dispose() }
$harness=[Reflection.Assembly]::LoadFrom("$repo\tools\MechKitHarness\bin\Release\MechKitHarness.exe")
$hostObject=[Activator]::CreateInstance($harness.GetType('MechKit.Harness.FakeHost'),$true)
$bomType=$assembly.GetType('MechKit.UI.PartListForm',$true)
$bom=[Activator]::CreateInstance($bomType,@($hostObject))
try {
    $bomType.GetMethod('RunSummary',$instanceFlags).Invoke($bom,@()) | Out-Null
    $grid=$bomType.GetField('_grid',$instanceFlags).GetValue($bom)
    $gridRow=@($grid.Rows | Where-Object {$null -ne $_.Tag})[0]
    $gridRow.Tag.FilePath=$model
    $gridRow.Cells['designnote'].Value='BOM design requirement'
    $bomType.GetMethod('SaveInlineProperty',$instanceFlags).Invoke($bom,@([int]$gridRow.Index,[int]$gridRow.Cells['designnote'].ColumnIndex)) | Out-Null
    $note=CallNote 'Load' @($model)
    if($note.Text -ne 'BOM design requirement' -or $note.Completed) { throw 'BOM inline edit failed' }
    $note=CallNote 'Save' @($model,[string]$fixture.note,$false)
    if($gridRow.Cells['designnote'].Value -ne $fixture.note) { throw 'BOM did not receive note update' }
} finally { $bom.Dispose() }
$paneType=$assembly.GetType('MechKit.UI.TaskPaneControl',$true)
$pane=[Activator]::CreateInstance($paneType,@($hostObject))
$form=New-Object Windows.Forms.Form
$form.ClientSize=New-Object Drawing.Size 360,900
$form.ShowInTaskbar=$false
$form.Opacity=0
$form.Controls.Add($pane)
try {
    $notes=$paneType.GetField('_designNotes',$instanceFlags).GetValue($pane)
    $panelType.GetMethod('Bind',$instanceFlags).Invoke($notes,@($model,[string]$fixture.model))
    $form.Show()
    [Windows.Forms.Application]::DoEvents()
    $bitmap=New-Object Drawing.Bitmap $form.Width,$form.Height
    try { $form.DrawToBitmap($bitmap,$form.ClientRectangle); $bitmap.Save((Join-Path $root 'properties-page.png')) }
    finally { $bitmap.Dispose() }
} finally { $form.Dispose() }
$note=CallNote 'Save' @($model,'',$false)
if($note.Text -or $note.CreatedAt -or $note.CompletedAt) { throw 'Clear failed' }
Write-Output 'PASS: creation/completion timestamps, reopen, edit, persistence, BOM lookup, checkbox save, document switch, clear'
Write-Output (Join-Path $root 'design-note.png')
Write-Output (Join-Path $root 'properties-page.png')
