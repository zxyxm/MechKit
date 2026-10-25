[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $repoRoot 'src\MechKit\bin\Release\MechKit.dll'))
$service = $assembly.GetType('MechKit.Features.ExportService', $true)
$formats = $assembly.GetType('MechKit.Features.ExportFormats', $true)
$optionsType = $assembly.GetType('MechKit.Features.BatchExportOptions', $true)
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('MechKit-export-' + [Guid]::NewGuid())
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$log = [Action[string]] { param($message) }
function Assert($condition, $message) {
    if (-not $condition) { throw $message }
}
try {
    $part = Join-Path $fixture 'bracket.SLDPRT'
    $drawing = Join-Path $fixture 'bracket.SLDDRW'
    $assemblyFile = Join-Path $fixture 'frame.sldasm'
    $assemblyDrawing = Join-Path $fixture 'frame.slddrw'
    foreach ($path in @($part, $drawing, $assemblyFile, $assemblyDrawing)) {
        [IO.File]::WriteAllText($path, '')
    }
    $resolve = $service.GetMethod('ResolveFiles')
    $paired = $resolve.Invoke($null, @([string[]]@($part, $drawing, $part.ToLowerInvariant()), $true, $log))
    Assert ($paired.Count -eq 2 -and $paired -contains $drawing) 'Part pairing or case-insensitive deduplication failed.'
    $reverse = $resolve.Invoke($null, @([string[]]@($drawing), $true, $log))
    Assert ($reverse.Count -eq 2 -and $reverse.Contains([IO.Path]::ChangeExtension($drawing, '.sldprt'))) 'Drawing-to-model pairing failed.'
    $unpaired = $resolve.Invoke($null, @([string[]]@($part), $false, $log))
    Assert ($unpaired.Count -eq 1) 'Disabled pairing added a companion.'
    $asmPair = $resolve.Invoke($null, @([string[]]@($assemblyDrawing), $true, $log))
    Assert ($asmPair.Count -eq 2 -and $asmPair.Contains($assemblyFile)) 'Assembly pairing failed.'
    $orphan = Join-Path $fixture 'orphan.sldprt'
    [IO.File]::WriteAllText($orphan, '')
    $missing = $resolve.Invoke($null, @([string[]]@($orphan), $true, $log))
    Assert ($missing.Count -eq 1) 'Missing companion was fabricated.'
    $quickExport = $assembly.GetType('MechKit.Features.QuickExport', $true)
    $matchField = $quickExport.GetMethod('MatchesFilter', [Reflection.BindingFlags]'NonPublic,Static')
    $naming = [Activator]::CreateInstance($assembly.GetType('MechKit.Core.NamingOptions'), $true)
    $naming.UseNameSegments = $true
    $sheetMetal = [string][char]0x94a3 + [char]0x91d1
    $factory = $assembly.GetType('MechKit.Core.NamingOptionsFactory')
    $rules = "304=304|CNC|`n6061=6061|CNC|`n5052=5052|$sheetMetal|`nd6061=6061|cnc-large|"
    $naming.MachinedMaterialProcessPresets = $factory.GetMethod('ParseMaterialProcessPresets').Invoke($null, @($rules))
    Assert ($matchField.Invoke($null, @('20261019-304-clamp.sldprt', 'cnc', $naming))) 'CNC rule not resolved.'
    Assert ($matchField.Invoke($null, @('20261019_6061_clamp.sldprt', 'CNC', $naming))) 'Same process across material fields not matched.'
    Assert ($matchField.Invoke($null, @('20261019-5052-clamp.sldprt', $sheetMetal, $naming))) 'Sheet metal rule not matched.'
    Assert ($matchField.Invoke($null, @('20261019-5052-clamp.sldprt', ('CNC,'+$sheetMetal), $naming))) 'Multiple processes not matched.'
    Assert (-not $matchField.Invoke($null, @('20261019-d6061-clamp.sldprt', 'CNC', $naming))) 'Process must match exactly.'
    Assert (-not $matchField.Invoke($null, @('20261019-304-clamp.sldprt', '304', $naming))) 'Material token must not match process.'
    Assert (-not $matchField.Invoke($null, @('plain.sldprt', 'CNC', $naming))) 'Unmapped part must not match CNC.'
    Assert ($matchField.Invoke($null, @('plain.sldprt', '', $naming))) 'Empty filter must include all parts.'
    $staticFlags = [Reflection.BindingFlags]'NonPublic,Static'
    $select = $quickExport.GetMethod('MatchesSelection', $staticFlags)
    $categoryState = $quickExport.GetField('CategoryFilter', $staticFlags)
    $secondaryState = $quickExport.GetField('FieldFilter', $staticFlags)
    $machined = -join ([char[]]@(0x52a0,0x5de5,0x4ef6))
    $standard = -join ([char[]]@(0x6807,0x51c6,0x4ef6))
    $all = -join ([char[]]@(0x5168,0x90e8))
    $vendor = -join ([char[]]@(0x6dd8,0x5b9d))
    $categoryState.SetValue($null, $machined)
    $secondaryState.SetValue($null, 'CNC')
    Assert ($select.Invoke($null, @('20261019-304-clamp.sldprt', $naming))) 'Machined CNC selection failed.'
    Assert (-not $select.Invoke($null, @('20261019-5052-clamp.sldprt', $naming))) 'Secondary process not applied.'
    $categoryState.SetValue($null, $standard)
    $secondaryState.SetValue($null, 'Motor')
    Assert ($select.Invoke($null, @(($vendor+'-Motor-model.sldprt'), $naming))) 'Standard middle-name selection failed.'
    Assert (-not $select.Invoke($null, @(($vendor+'-Bolt-model.sldprt'), $naming))) 'Standard secondary filter not applied.'
    Assert (-not $select.Invoke($null, @('20261019-304-clamp.sldprt', $naming))) 'Category filter leaked machined parts.'
    $categoryState.SetValue($null, $all)
    Assert ($select.Invoke($null, @('plain.sldprt', $naming))) 'All categories must ignore stale secondary values.'
    $secondaryState.SetValue($null, '')
    Assert ([bool]$quickExport.GetField('Overwrite', [Reflection.BindingFlags]'NonPublic,Static').GetValue($null)) 'Quick export must refresh existing outputs by default.'
    Assert (-not [bool]$quickExport.GetField('OnlyWithDrawing', [Reflection.BindingFlags]'NonPublic,Static').GetValue($null)) 'All parts must be included by default.'
    $collectPairs = $quickExport.GetMethod('CollectPairedParts', [Reflection.BindingFlags]'NonPublic,Static')
    $nestedFolder = Join-Path $fixture 'nested'
    [IO.Directory]::CreateDirectory($nestedFolder) | Out-Null
    $nestedPart = Join-Path $nestedFolder 'child.sldprt'
    [IO.File]::WriteAllText($nestedPart, '')
    [IO.File]::WriteAllText([IO.Path]::ChangeExtension($nestedPart, '.slddrw'), '')
    $quickFiles = $collectPairs.Invoke($null, @([string[]]@($part, $part.ToLowerInvariant(), $orphan, $assemblyFile, $drawing, $nestedPart), $log))
    Assert ($quickFiles.Count -eq 2 -and $quickFiles.Contains($part) -and $quickFiles.Contains($nestedPart)) 'Quick export must include paired parts only, deduplicated across nested assemblies.'
    $quickPairs = $resolve.Invoke($null, @([string[]]$quickFiles.ToArray(), $true, $log))
    Assert ($quickPairs.Count -eq 4) 'Quick export must produce one model and one drawing per paired part.'
    $emptyQuick = $collectPairs.Invoke($null, @([string[]]@($orphan, $assemblyFile), $log))
    Assert ($emptyQuick.Count -eq 0) 'Quick export must not fall back to the assembly when no paired parts exist.'
    $step = $formats.GetMethod('Find').Invoke($null, @('.step'))
    Assert ($step.Extension -eq '.stp') 'Legacy STEP setting was not migrated.'
    $pdf = $formats.GetField('Pdf').GetValue($null)
    Assert ($pdf.Supports(3) -and -not $pdf.Supports(1) -and -not $pdf.Supports(2)) 'PDF must export drawings only.'
    Assert ($step.Supports(1) -and $step.Supports(2) -and -not $step.Supports(3)) 'STP must export models only.'
    $options = [Activator]::CreateInstance($optionsType, $true)
    $options.SourceRoot = $fixture
    $options.OutputFolder = Join-Path $fixture 'out'
    $options.KeepTree = $true
    $targetMethod = $service.GetMethod('BuildTargetPath', [Reflection.BindingFlags]'NonPublic,Static')
    $target = $targetMethod.Invoke($null, @($options, [string]$part, $step))
    Assert ($target -eq (Join-Path $options.OutputFolder 'bracket.stp')) 'Root file gained an extra output directory.'
    $nested = Join-Path $fixture 'sub\bracket.sldprt'
    $nestedTarget = $targetMethod.Invoke($null, @($options, [string]$nested, $step))
    Assert ($nestedTarget -eq (Join-Path $options.OutputFolder 'sub\bracket.stp')) 'Nested directory was not preserved.'
    $options.Files.Add($part)
    $options.Formats.Add($step)
    $options.AutoPair = $false
    $options.Overwrite = $false
    $options.CreateReport = $false
    [IO.Directory]::CreateDirectory($options.OutputFolder) | Out-Null
    [IO.File]::WriteAllText($target, 'existing output')
    $report = $service.GetMethod('Run').Invoke($null, @($null, $options, $log, [Func[bool]] { $false }, $null))
    Assert ($report.Skipped -eq 1 -and $report.Failed -eq 0 -and $report.Exported -eq 0) 'Existing output was counted as failure.'
    Assert ([IO.File]::ReadAllText($target) -eq 'existing output') 'Existing output was modified.'
    Add-Type -AssemblyName System.Windows.Forms
    $formType = $assembly.GetType('MechKit.UI.BatchExportForm', $true)
    $tree = [System.Windows.Forms.TreeView]::new()
    $rootNode = [System.Windows.Forms.TreeNode]::new('assembly')
    $rootNode.Tag = $assemblyFile
    $rootNode.Checked = $true
    $childNode = [System.Windows.Forms.TreeNode]::new('part')
    $childNode.Tag = $part
    $childNode.Checked = $true
    $duplicateNode = [System.Windows.Forms.TreeNode]::new('second instance')
    $duplicateNode.Tag = $part
    $duplicateNode.Checked = $true
    $rootNode.Nodes.Add($childNode) | Out-Null
    $rootNode.Nodes.Add($duplicateNode) | Out-Null
    $tree.Nodes.Add($rootNode) | Out-Null
    $checkedFiles = [System.Collections.Generic.List[string]]::new()
    $collectTree = $formType.GetMethod('CollectCheckedFiles', [Reflection.BindingFlags]'NonPublic,Static')
    $collectTree.Invoke($null, @($tree.Nodes, $checkedFiles))
    Assert ($checkedFiles.Count -eq 2) 'Assembly tree instances were not deduplicated.'
    $setTree = $formType.GetMethod('SetChildrenChecked', [Reflection.BindingFlags]'NonPublic,Static')
    $setTree.Invoke($null, @($rootNode, $false))
    $checkedFiles.Clear()
    $collectTree.Invoke($null, @($tree.Nodes, $checkedFiles))
    Assert ($checkedFiles.Count -eq 1 -and -not $childNode.Checked) 'Tree child selection did not propagate.'
    $tree.Dispose()
    Write-Host 'PASS: assembly tree selection and instance deduplication.'
    Write-Host 'PASS: pairing, deduplication, missing companion, format routing, legacy settings, output paths, overwrite skipping.'
}
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedFixture.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Fixture cleanup path escaped the temporary directory.'
    }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
