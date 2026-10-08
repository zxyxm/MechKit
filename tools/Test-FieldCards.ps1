$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$repoRoot = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $repoRoot 'src\MechKit\bin\Release\MechKit.dll'))
$type = $assembly.GetType('MechKit.UI.NamingRuleForm', $true)
$arrange = $type.GetMethod('ArrangeFieldCards', [Reflection.BindingFlags]'NonPublic,Static')
$panel = [System.Windows.Forms.TableLayoutPanel]::new()
$panel.ColumnCount = 1
$panel.RowCount = 5
try {
    for ($i = 0; $i -lt 5; $i++) {
        $card = [System.Windows.Forms.TableLayoutPanel]::new()
        $card.ColumnCount = 4
        $card.RowCount = 1
        $label = [System.Windows.Forms.Label]::new()
        $label.Text = 'Field ' + $i
        $description = [System.Windows.Forms.TextBox]::new()
        $description.Text = 'Description ' + $i
        $check = [System.Windows.Forms.CheckBox]::new()
        $check.Checked = $true
        $delete = [System.Windows.Forms.Button]::new()
        $card.Controls.Add($label,0,0)
        $card.Controls.Add($description,1,0)
        $card.Controls.Add($check,2,0)
        $card.Controls.Add($delete,3,0)
        $panel.Controls.Add($card,0,$i)
    }
    $color = [System.Drawing.Color]::FromArgb(255,246,210)
    $arrange.Invoke($null,@($panel,$color,5))
    if ($panel.ColumnCount -ne 4 -or $panel.RowCount -ne 2) { throw 'Wrong grid dimensions.' }
    for ($i = 0; $i -lt 5; $i++) {
        $card = $panel.GetControlFromPosition(($i % 4),[int][Math]::Floor($i / 4))
        if ($null -eq $card -or $card.BackColor -ne $color) { throw 'Wrong card position or color.' }
        if ($card.GetControlFromPosition(0,1).Text -ne ('Description ' + $i)) { throw 'Description was lost.' }
        if (-not $card.GetControlFromPosition(1,0).Checked) { throw 'Selection was lost.' }
    }
    Write-Output 'PASS: four columns, wrapping, pastel backgrounds, descriptions and selections preserved.'
}
finally { $panel.Dispose() }
