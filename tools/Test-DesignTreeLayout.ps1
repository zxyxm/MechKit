[CmdletBinding()]
param([string] $PreviewPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$repo = Split-Path -Parent $PSScriptRoot
$harness = [Reflection.Assembly]::LoadFrom((Join-Path $repo 'tools\MechKitHarness\bin\Release\MechKitHarness.exe'))
$addin = [Reflection.Assembly]::LoadFrom((Join-Path $repo 'tools\MechKitHarness\bin\Release\MechKit.dll'))
$hostType = $harness.GetType('MechKit.Harness.FakeHost', $true)
$formType = $addin.GetType('MechKit.UI.PartListForm', $true)
$itemType = $addin.GetType('MechKit.Features.DesignTreeItem', $true)
$kindType = $addin.GetType('MechKit.Core.DesignNodeKind', $true)
$flags = [Reflection.BindingFlags]'Instance, NonPublic'
$fakeHost = [Activator]::CreateInstance($hostType, $true)
$form = [Activator]::CreateInstance($formType, @($fakeHost))
$checks = 0
function Check($label, $condition) {
    if (-not $condition) { throw "FAIL: $label" }
    $script:checks++
    Write-Host "PASS: $label"
}
function Item($name, $kind, $row) {
    $item = [Activator]::CreateInstance($itemType)
    $item.Name = $name
    $item.Kind = [Enum]::Parse($kindType, $kind)
    if ($null -ne $row) { $item.Rows.Add($row) }
    return $item
}
try {
    $form.CreateControl()
    $formType.GetMethod('RunSummary', $flags).Invoke($form, @())
    $rows = $formType.GetField('_rows', $flags).GetValue($form)
    $items = $formType.GetField('_treeItems', $flags).GetValue($form)
    $rows[0].Process = '钣金'
    $rows[0].DrawingPath = ''
    $items.Clear()
    $assemblyNode = Item '20261010-装配-测试' 'Assembly' $null
    $groupNode = Item '20261010-组件-分组' 'Group' $null
    $groupNode.Children.Add((Item $rows[0].FullName 'Machined' $rows[0]))
    $referenceNode = Item '参考-治具' 'Reference' $null
    $referenceNode.Children.Add((Item '参考件内部零件' 'Machined' $null))
    $groupNode.Children.Add($referenceNode)
    $assemblyNode.Children.Add($groupNode)
    $assemblyNode.Children.Add((Item $rows[1].FullName 'Standard' $rows[1]))
    $items.Add($assemblyNode)
    $items.Add((Item $rows[2].FullName 'Standard' $rows[2]))
    foreach ($category in @(@('Stock', '库存件', '库存-电机'), @('Spare', '备件', '备件-密封圈'), @('ExternalDrawing', '外部图纸', 'BRC-供应商图纸'))) {
        $categoryRow = [Activator]::CreateInstance($rows[0].GetType())
        $categoryRow.FullName = $category[2]
        $categoryRow.Classification = $category[1]
        $categoryRow.Name = $category[2]
        $categoryRow.Quantity = 1
        $rows.Add($categoryRow)
        $items.Add((Item $category[2] $category[0] $categoryRow))
    }
    $formType.GetMethod('ShowDesignTree', $flags).Invoke($form, @())
    $formType.GetMethod('ShowFilteredRows', $flags).Invoke($form, @($false))
    $grid = $formType.GetField('_grid', $flags).GetValue($form)
    $tree = $formType.GetField('_designTree', $flags).GetValue($form)
    $assemblyTree = $tree.Nodes[0].Nodes[0]
    $groupTree = $assemblyTree.Nodes[0]
    Check 'assembly and group share pale purple' ($assemblyTree.BackColor -eq $groupTree.BackColor -and $assemblyTree.BackColor -eq [Drawing.Color]::FromArgb(238, 230, 250))
    $referenceTree = $groupTree.Nodes[1]
    Check 'reference is gray text without background or expansion' ($referenceTree.Nodes.Count -eq 0 -and -not $referenceTree.IsExpanded -and $referenceTree.BackColor -eq [Drawing.Color]::Empty -and $referenceTree.ForeColor -ne [Drawing.Color]::Empty)
    Check 'containers and excluded nodes create aligned gaps' ($grid.Rows.Count -eq 10)
    $dataRows = @($grid.Rows | Where-Object { $null -ne $_.Tag })
    $sheetGridRow = @($dataRows | Where-Object { $_.Tag -eq $rows[0] })[0]
    Check 'sheet metal without drawing displays sheet-metal label' ($sheetGridRow.Cells['drawing'].Value -eq '钣金')
    $missingColor = $formType.GetField('MissingDrawingRowColor', [Reflection.BindingFlags]'Static, NonPublic').GetValue($null)
    Check 'sheet metal without drawing is not painted as missing drawing' ($sheetGridRow.Cells['drawing'].Style.BackColor -ne $missingColor)
    Check 'exactly one BOM row per recognized leaf' ($dataRows.Count -eq 6)
    Check 'design tree and connectors remain frozen on horizontal scroll' ($grid.Columns['designTree'].Frozen -and $grid.Columns['treeLink'].Frozen)
    foreach ($row in $dataRows) {
        $node = $row.Cells['designTree'].Tag
        Check 'BOM stays on the same row as its tree node' ($node.Text.Contains($row.Tag.FullName))
        Check 'recognized tree node has a background color' ($node.BackColor -ne [Drawing.Color]::Empty)
        Check 'all BOM cells including editable cells match the design tree color' (@($row.Cells | Where-Object { $_.Style.BackColor -ne $node.BackColor }).Count -eq 0)
    }
    $rows[0].Process = 'cnc'
    $formType.GetMethod('ShowFilteredRows', $flags).Invoke($form, @($false))
    $missingRow = @($grid.Rows | Where-Object { $_.Tag -eq $rows[0] })[0]
    Check 'missing machining drawing paints only the drawing cell red' ($missingRow.Cells['drawing'].Style.BackColor -eq $missingColor -and @($missingRow.Cells | Where-Object { $_.Style.BackColor -eq $missingColor }).Count -eq 1)
    Check 'missing drawing leaves design tree green' ($missingRow.Cells['designTree'].Style.BackColor -eq [Drawing.Color]::FromArgb(232, 246, 233))
    $rows[0].Process = '钣金'
    $rows[1].DrawingPath = 'C:\test\standard.SLDDRW'
    $formType.GetMethod('ShowFilteredRows', $flags).Invoke($form, @($false))
    $standardRow = @($grid.Rows | Where-Object { $_.Tag -eq $rows[1] })[0]
    Check 'standard with drawing remains blue' (@($standardRow.Cells | Where-Object { $_.Style.BackColor -ne [Drawing.Color]::FromArgb(231, 242, 255) }).Count -eq 0)
    $externalRow = @($grid.Rows | Where-Object { $null -ne $_.Tag -and $_.Tag.Classification -eq '外部图纸' })[0]
    Check 'external drawing is yellow across the whole row' (@($externalRow.Cells | Where-Object { $_.Style.BackColor -ne [Drawing.Color]::FromArgb(255, 247, 210) }).Count -eq 0)
    $externalItem = $items[$items.Count - 1]
    $externalBom = $externalItem.Rows[0]
    $externalBom.Classification = '命名不规范'
    $externalBom.IsUnmatched = $true
    $externalItem.Kind = [Enum]::Parse($kindType, 'Unmatched')
    $formType.GetMethod('ShowDesignTree', $flags).Invoke($form, @())
    $formType.GetMethod('ShowFilteredRows', $flags).Invoke($form, @($false))
    $unmatchedRow = @($grid.Rows | Where-Object { $_.Tag -eq $externalBom })[0]
    Check 'unmatched naming is red across tree connector and every BOM cell' (@($unmatchedRow.Cells | Where-Object { $_.Style.BackColor -ne $missingColor }).Count -eq 0)
    Check 'unmatched naming is distinct from external drawings' ($unmatchedRow.Cells['classification'].Value -eq '命名不规范' -and $unmatchedRow.Cells['designTree'].Value.Contains('[命名不规范]'))
    $externalBom.Classification = '外部图纸'
    $externalBom.IsUnmatched = $false
    $externalItem.Kind = [Enum]::Parse($kindType, 'ExternalDrawing')
    $formType.GetMethod('ShowDesignTree', $flags).Invoke($form, @())
    $formType.GetMethod('ShowFilteredRows', $flags).Invoke($form, @($false))
    if ($PreviewPath) {
        $bitmap = New-Object Drawing.Bitmap($grid.Width, $grid.Height)
        try {
            $grid.DrawToBitmap($bitmap, (New-Object Drawing.Rectangle(0, 0, $grid.Width, $grid.Height)))
            $bitmap.Save([IO.Path]::GetFullPath($PreviewPath), [Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $bitmap.Dispose() }
    }
    $grid.Sort($grid.Columns['name'], [ComponentModel.ListSortDirection]::Descending)
    Check 'manual sort suspends design connectors' (-not $formType.GetField('_designOrder', $flags).GetValue($form))
    Check 'manual sort hides layout gaps' (@($grid.Rows | Where-Object Visible).Count -eq 6)
    $formType.GetMethod('ShowFilteredRows', $flags).Invoke($form, @($false))
    Check 'default sort restores tree alignment' ($grid.Rows.Count -eq 10 -and $formType.GetField('_designOrder', $flags).GetValue($form))
    $tree.Nodes[0].Nodes[0].Nodes[0].Collapse()
    $formType.GetMethod('ShowFilteredRows', $flags).Invoke($form, @($false))
    Check 'collapse hides descendants on both sides together' ($grid.Rows.Count -eq 8)
    Check 'collapse does not remove BOM records used for export' ($rows.Count -eq 6)
    $filter = $formType.GetField('_classificationFilter', $flags).GetValue($form)
    $filter.SelectedIndex = 3
    Check 'inventory filter shows only inventory BOM rows' (@($grid.Rows | Where-Object { $null -ne $_.Tag }).Count -eq 1)
    Check 'inventory filter preserves category label' (@($grid.Rows | Where-Object { $null -ne $_.Tag })[0].Tag.Classification -eq '库存件')
    $filter.SelectedIndex = 4
    Check 'spare filter shows only spare BOM rows' (@($grid.Rows | Where-Object { $null -ne $_.Tag }).Count -eq 1)
    Check 'spare filter preserves category label' (@($grid.Rows | Where-Object { $null -ne $_.Tag })[0].Tag.Classification -eq '备件')
    $filter.SelectedIndex = 5
    Check 'external filter shows one whole drawing item' (@($grid.Rows | Where-Object { $null -ne $_.Tag }).Count -eq 1)
    Check 'external filter preserves drawing category' (@($grid.Rows | Where-Object { $null -ne $_.Tag })[0].Tag.Classification -eq '外部图纸')
    $panelType = $addin.GetType('MechKit.UI.CategoryRulesPanel', $true)
    $categoryPanel = [Activator]::CreateInstance($panelType, @($fakeHost, -1))
    try {
        $editors = $panelType.GetField('_editors', $flags).GetValue($categoryPanel)
        $editors[0].Text = '库存,KST'
        $editors[1].Text = '备件;SPARE'
        $editors[2].Text = ' BRC, SUP;外来 '
        $categoryPanel.SaveToSettings()
        Check 'category editor saves normalized external prefixes' ($fakeHost.Settings.ExternalDrawingPrefixes -eq 'BRC SUP 外来')
        Check 'category editor saves stock prefixes' ($fakeHost.Settings.StockPrefixes -eq '库存 KST')
        Check 'category editor saves spare prefixes' ($fakeHost.Settings.SparePrefixes -eq '备件 SPARE')
        $portableFile = Join-Path ([IO.Path]::GetTempPath()) ('MechKit-category-test-' + [Guid]::NewGuid() + '.ini')
        try {
            $message = ''
            Check 'category settings export succeeds' ($fakeHost.Settings.ExportPortable($portableFile, [ref]$message))
            $settingsType = $fakeHost.Settings.GetType()
            $reloaded = [Activator]::CreateInstance($settingsType)
            $settingsType.GetMethod('LoadFile', [Reflection.BindingFlags]'Static, NonPublic').Invoke($null, @($reloaded, [string]$portableFile))
            Check 'external prefixes survive configuration export and reload' ($reloaded.ExternalDrawingPrefixes -eq 'BRC SUP 外来')
            Check 'stock prefixes survive configuration export and reload' ($reloaded.StockPrefixes -eq '库存 KST')
            Check 'spare prefixes survive configuration export and reload' ($reloaded.SparePrefixes -eq '备件 SPARE')
        }
        finally { if (Test-Path -LiteralPath $portableFile) { Remove-Item -LiteralPath $portableFile } }
        $fakeHost.Settings.ExternalDrawingPrefixes = 'BRC- SUP-'
        $categoryPanel.LoadFromSettings()
        $externalEditors = $panelType.GetField('_externalEditors', $flags).GetValue($categoryPanel)
        $externalRows = $panelType.GetField('_externalRows', $flags).GetValue($categoryPanel)
        Check 'saved external prefixes load as separate numbered rows' ($externalEditors.Count -eq 2 -and $externalEditors[0].Text -eq 'BRC-' -and $externalEditors[1].Text -eq 'SUP-')
        @($externalRows.Controls | Where-Object { $_.Text -eq '添加外部图纸前缀' })[0].PerformClick()
        $externalEditors[2].Text = 'EXT-'
        $categoryPanel.SaveToSettings()
        Check 'adding external prefix preserves previous rows and punctuation' ($fakeHost.Settings.ExternalDrawingPrefixes -eq 'BRC- SUP- EXT-')
        $removeButtons = @($externalRows.Controls | Where-Object { $_.Text -eq '删除' })
        $removeButtons[1].PerformClick()
        $categoryPanel.SaveToSettings()
        Check 'deleting one prefix preserves the others' ($fakeHost.Settings.ExternalDrawingPrefixes -eq 'BRC- EXT-')
        $namingFactory = $addin.GetType('MechKit.Core.NamingOptionsFactory', $true)
        $exactNaming = $namingFactory.GetMethod('FromSettings').Invoke($null, @($fakeHost.Settings))
        Check 'external prefix retains hyphen for exact starts-with matching' ($exactNaming.RecognizeDesignNode('BRC-支架').ToString() -eq 'ExternalDrawing' -and $exactNaming.RecognizeDesignNode('BRC001').ToString() -eq 'Unmatched')
        $categoryPanel.Size = New-Object Drawing.Size(900, 420)
        $previewForm = New-Object Windows.Forms.Form
        $previewForm.ClientSize = New-Object Drawing.Size(900, 420)
        $previewForm.Controls.Add($categoryPanel)
        $previewForm.Show()
        [Windows.Forms.Application]::DoEvents()
        $categoryPreview = New-Object Drawing.Bitmap(900, 420)
        try { $categoryPanel.DrawToBitmap($categoryPreview, (New-Object Drawing.Rectangle(0, 0, 900, 420))); $categoryPreview.Save((Join-Path $env:TEMP 'MechKit-external-prefixes.png')) }
        finally { $categoryPreview.Dispose(); $previewForm.Close(); $previewForm.Dispose() }
    }
    finally { $categoryPanel.Dispose() }
    $namingType = $addin.GetType('MechKit.UI.NamingRuleForm', $true)
    $namingForm = [Activator]::CreateInstance($namingType, @($fakeHost, 5))
    try {
        $pending = New-Object Collections.Queue
        $pending.Enqueue($namingForm)
        $tabs = $null
        while ($pending.Count -gt 0) {
            $control = $pending.Dequeue()
            if ($control -is [Windows.Forms.TabControl]) { $tabs = $control; break }
            foreach ($child in $control.Controls) { $pending.Enqueue($child) }
        }
        Check 'naming rules expose all six categories' ($tabs.TabPages.Count -eq 6)
        Check 'external drawing command opens the correct category' ($tabs.SelectedTab.Text -eq '外部图纸')
    }
    finally { $namingForm.Dispose() }
    Write-Host "All $checks design tree layout checks passed."
}
finally { $form.Dispose() }
